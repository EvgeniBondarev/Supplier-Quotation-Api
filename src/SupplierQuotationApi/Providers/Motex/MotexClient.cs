using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Providers.Motex;

/// <summary>Ошибка МоТехС. <see cref="StatusCode"/> 404 — «нет предложений», штатный частый ответ, а не сбой.</summary>
public sealed class MotexApiException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

/// <summary>Bearer-токен МоТехС: один на процесс, обновляется под замком. Хранится только в памяти.</summary>
public sealed class MotexTokenCache
{
    private readonly IOptions<MotexOptions> _options;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public MotexTokenCache(IOptions<MotexOptions> options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
    }

    public async Task<string> GetAsync(HttpClient http, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && IsValid()) return _token!;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && IsValid()) return _token!;

            var o = _options.Value;
            using var response = await http.PostAsJsonAsync("api/v1/Auth/login",
                new { login = o.Login, password = o.Password }, cancellationToken);
            var login = await MotexClient.ReadEnvelopeAsync<MotexLoginResponse>(response, "Auth/login", cancellationToken);
            if (string.IsNullOrWhiteSpace(login?.Token))
                throw new MotexApiException("Ответ авторизации МоТехС не содержит token.");

            _token = login.Token;
            _expiresAt = _clock.GetUtcNow().AddSeconds(Math.Max(60, login.ExpireIn ?? 3600));
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsValid() => !string.IsNullOrEmpty(_token) && _expiresAt > _clock.GetUtcNow().AddMinutes(1);
}

/// <summary>Клиент МоТехС. Ответы обёрнуты в {status, messages, response}; наружу отдаётся только response.</summary>
public sealed class MotexClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly MotexTokenCache _tokens;

    public MotexClient(HttpClient http, MotexTokenCache tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    /// <summary>404 (в статусе конверта или HTTP) — нет предложений: пустой список.</summary>
    public async Task<List<MotexArticle>> SearchAsync(string code, string? brand, string? deliveryAddressCode,
        CancellationToken cancellationToken)
    {
        var query = $"code={Uri.EscapeDataString(code)}&withAnalogs=0";
        if (!string.IsNullOrWhiteSpace(brand)) query += $"&brand={Uri.EscapeDataString(brand)}";
        if (!string.IsNullOrWhiteSpace(deliveryAddressCode)) query += $"&deliveryAddressCode={Uri.EscapeDataString(deliveryAddressCode)}";

        try
        {
            return (await GetAsync<MotexArticlesResponse>($"api/v1/Articles/Search?{query}", cancellationToken))?.Articles ?? [];
        }
        catch (MotexApiException ex) when (ex.StatusCode == 404)
        {
            return [];
        }
    }

    public async Task<List<MotexAddress>> GetDeliveryAddressesAsync(CancellationToken cancellationToken) =>
        (await GetAsync<MotexAddressesResponse>("api/v1/Users/GetUserDeliveryAddresses", cancellationToken))?.DeliveryAddresses ?? [];

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await SendOnceAsync(path, false, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            using var retry = await SendOnceAsync(path, true, ct);
            return await ReadEnvelopeAsync<T>(retry, path, ct);
        }
        return await ReadEnvelopeAsync<T>(response, path, ct);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(string path, bool refreshToken, CancellationToken ct)
    {
        var token = await _tokens.GetAsync(_http, refreshToken, ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(request, ct);
    }

    public static async Task<T?> ReadEnvelopeAsync<T>(HttpResponseMessage response, string path, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);

        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(text, Json); }
        catch (JsonException)
        {
            if (!response.IsSuccessStatusCode)
                throw new MotexApiException($"МоТехС вернул HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
            throw new MotexApiException("МоТехС вернул невалидный JSON.");
        }

        // Ошибка приходит и телом-конвертом (например 403 «Проверьте IP адрес»), и статусом HTTP: конверт точнее.
        var status = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Number
            ? s.GetInt32()
            : (int)response.StatusCode;
        if (status != 200)
            throw new MotexApiException(ErrorText(root) ?? $"МоТехС вернул статус {status}.", status);
        if (!response.IsSuccessStatusCode)
            throw new MotexApiException($"МоТехС вернул HTTP {(int)response.StatusCode}.", (int)response.StatusCode);

        return root.TryGetProperty("response", out var body) ? body.Deserialize<T>(Json) : default;
    }

    /// <summary>messages — объект {errorText} (реальная форма) или массив строк.</summary>
    private static string? ErrorText(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages)) return null;
        return messages.ValueKind switch
        {
            JsonValueKind.Object when messages.TryGetProperty("errorText", out var t) && t.ValueKind == JsonValueKind.String => t.GetString(),
            JsonValueKind.Object => messages.ToString(),
            JsonValueKind.Array => string.Join("; ", messages.EnumerateArray().Select(x => x.ToString())),
            JsonValueKind.String => messages.GetString(),
            _ => null
        };
    }
}
