using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Providers.ShateM;

/// <summary>Bearer-токен Шате-М: один на процесс, обновляется под замком. Хранится только в памяти.</summary>
public sealed class ShateMTokenCache
{
    private readonly IOptions<ShateMOptions> _options;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ShateMTokenCache(IOptions<ShateMOptions> options, TimeProvider clock)
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
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["login"] = o.Login ?? string.Empty,
                ["password"] = o.Password ?? string.Empty
            });
            using var response = await http.PostAsync("api/v1/auth/login", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Авторизация Шате-М завершилась HTTP {(int)response.StatusCode}.");

            var token = await response.Content.ReadFromJsonAsync<ShateMToken>(cancellationToken);
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
                throw new InvalidOperationException("Ответ авторизации Шате-М не содержит access_token.");

            _token = token.AccessToken;
            _expiresAt = _clock.GetUtcNow().AddSeconds(Math.Max(60, token.ExpiresIn ?? 3600));
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsValid() => !string.IsNullOrEmpty(_token) && _expiresAt > _clock.GetUtcNow().AddMinutes(1);
}
