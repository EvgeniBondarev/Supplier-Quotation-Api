using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SupplierQuotationApi.Providers.Armtek;

public sealed class ArmtekApiException(string message) : Exception(message);

/// <summary>Клиент одного аккаунта Armtek Web-Services: Basic Auth, form-urlencoded, конверт STATUS/MESSAGES/RESP.</summary>
public sealed class ArmtekClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan QuotaBlock = TimeSpan.FromMinutes(30);
    private const string QuotaText = "Превышено количество запросов";

    private readonly HttpClient _http;
    private readonly string _authorization;
    private readonly TimeProvider _clock;
    private DateTimeOffset _quotaBlockedUntil = DateTimeOffset.MinValue;

    public ArmtekClient(HttpClient http, ArmtekOptions options, TimeProvider clock)
    {
        _http = http;
        _clock = clock;
        _authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.User}:{options.Password}"));
    }

    /// <summary>POST form. Возвращает RESP как T; «ничего не найдено» (RESP-объект вместо массива) — default.
    /// Ошибки бизнес-уровня (MESSAGES E/A) — <see cref="ArmtekApiException"/>.</summary>
    public async Task<T?> PostAsync<T>(string path, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        if (_quotaBlockedUntil > _clock.GetUtcNow())
            throw new ArmtekApiException("Превышено суточное количество запросов к API Armtek для этого логина.");

        var body = new Dictionary<string, string>(form) { ["format"] = "json" };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/{path}?format=json")
        {
            Content = new FormUrlEncodedContent(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authorization);

        using var response = await _http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (IsQuotaExceeded(text)) BlockQuota();
        if (!response.IsSuccessStatusCode)
            throw new ArmtekApiException(_quotaBlockedUntil > _clock.GetUtcNow()
                ? "Превышено суточное количество запросов к API Armtek для этого логина."
                : $"Armtek вернул HTTP {(int)response.StatusCode} на {path}.");

        ArmtekEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<ArmtekEnvelope>(text, Json); }
        catch (JsonException) { throw new ArmtekApiException($"Armtek вернул невалидный ответ на {path}."); }
        if (envelope is null) throw new ArmtekApiException($"Armtek вернул пустой ответ на {path}.");

        var errors = envelope.Messages?.Where(m => m.Type is "E" or "A" && !string.IsNullOrWhiteSpace(m.Text))
            .Select(m => m.Text!.Trim()).ToList() ?? [];
        if (errors.Count > 0) throw new ArmtekApiException(string.Join("; ", errors));

        if (envelope.Resp.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return default;
        try { return envelope.Resp.Deserialize<T>(Json); }
        catch (JsonException) { return default; }
    }

    private void BlockQuota() => _quotaBlockedUntil = _clock.GetUtcNow().Add(QuotaBlock);

    private static bool IsQuotaExceeded(string text)
    {
        if (text.Contains(QuotaText, StringComparison.OrdinalIgnoreCase)) return true;
        if (!text.Contains(@"\u", StringComparison.Ordinal)) return false;
        try { return Regex.Unescape(text).Contains(QuotaText, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }
}
