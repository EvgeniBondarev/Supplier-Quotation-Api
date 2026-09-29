using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SupplierQuotationApi.Providers.ShateM;

/// <summary>HTTP-клиент Шате-М. Регистрируется через AddProviderHttpClient (пул, retry, breaker).</summary>
public sealed class ShateMClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly ShateMTokenCache _tokens;

    public ShateMClient(HttpClient http, ShateMTokenCache tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    /// <summary>Поиск артикулов по строке без спецсимволов. Возвращает все бренды с таким кодом.</summary>
    public async Task<List<ShateMArticle>> SearchArticlesAsync(string code, CancellationToken cancellationToken)
    {
        var refs = await SendAsync<List<ShateMArticleRef>>(HttpMethod.Get,
            $"api/v1/articles/search/{Uri.EscapeDataString(code)}", null, cancellationToken);
        return refs?.Select(x => x.Article).ToList() ?? [];
    }

    /// <summary>Цены сразу по нескольким артикулам, сгруппированные по артикулу.</summary>
    public async Task<List<ShateMArticlePrices>> SearchPricesAsync(IEnumerable<int> articleIds, bool includeAnalogs,
        CancellationToken cancellationToken)
    {
        var body = articleIds.Select(id => new { articleId = id, includeAnalogs }).ToList();
        return await SendAsync<List<ShateMArticlePrices>>(HttpMethod.Post, "api/v1/prices/search/with_article_info",
            body, cancellationToken) ?? [];
    }

    public async Task<List<ShateMLocation>> GetLocationsAsync(CancellationToken cancellationToken) =>
        await SendAsync<List<ShateMLocation>>(HttpMethod.Get, "api/v1/locations", null, cancellationToken) ?? [];

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendOnceAsync(method, path, body, false, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            using var retry = await SendOnceAsync(method, path, body, true, cancellationToken);
            return await ReadAsync<T>(retry, path, cancellationToken);
        }

        return await ReadAsync<T>(response, path, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body,
        bool refreshToken, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAsync(_http, refreshToken, cancellationToken);
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        return await _http.SendAsync(request, cancellationToken);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, string path, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Шате-М вернул HTTP {(int)response.StatusCode} на {path}.");
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }
}
