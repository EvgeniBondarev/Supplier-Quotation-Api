using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Providers.Nikei;

public sealed class NikeiApiException(string message) : Exception(message);

public sealed class NikeiPart
{
    [JsonPropertyName("product_key")] public string? ProductKey { get; set; }
    [JsonPropertyName("article")] public string? Article { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>Остаток. −1 или отсутствие — не указан.</summary>
    [JsonPropertyName("stock")] public int? Stock { get; set; }
    [JsonPropertyName("multiplicity")] public int? Multiplicity { get; set; }
    [JsonPropertyName("deliverytime")] public NikeiDelivery? DeliveryTime { get; set; }
    /// <summary>Процент выдачи (надёжность поставки).</summary>
    [JsonPropertyName("percent")] public int? Percent { get; set; }
    [JsonPropertyName("price")] public decimal? Price { get; set; }
    [JsonPropertyName("warehouse")] public string? Warehouse { get; set; }
    [JsonPropertyName("returnable")] public bool? Returnable { get; set; }
    [JsonPropertyName("return_cost")] public int? ReturnCost { get; set; }
    [JsonPropertyName("comment")] public string? Comment { get; set; }
    [JsonPropertyName("unsafe")] public bool? Unsafe { get; set; }
    /// <summary>Токен добавления в корзину Nikei (нужен для будущего заказа).</summary>
    [JsonPropertyName("to_cart")] public string? ToCart { get; set; }
}

public sealed class NikeiDelivery
{
    [JsonPropertyName("min")] public int? Min { get; set; }
    [JsonPropertyName("max")] public int? Max { get; set; }
}

public sealed class NikeiPartsResponse
{
    [JsonPropertyName("parts")] public List<NikeiPart>? Parts { get; set; }
    /// <summary>Без бренда Nikei отдаёт не предложения, а список брендов номера.</summary>
    [JsonPropertyName("groups")] public List<NikeiGroup>? Groups { get; set; }
}

public sealed class NikeiGroup
{
    [JsonPropertyName("brand")] public string? Brand { get; set; }
}

/// <summary>Клиент API Nikei. Basic Auth только в заголовке. Значения идут сегментами пути и экранируются.
/// Бизнес-ошибка может прийти с HTTP 200: {"status":"error","message":"…"}.</summary>
public sealed class NikeiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly string _authorization;

    public NikeiClient(HttpClient http, IOptions<NikeiOptions> options)
    {
        _http = http;
        var o = options.Value;
        _authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.Login}:{o.Password}"));
    }

    /// <summary>Точная проценка бренда; filterByArticle убирает аналоги на стороне Nikei.</summary>
    public async Task<List<NikeiPart>> SearchAsync(string article, string brand, CancellationToken ct) =>
        (await GetAsync($"parts/search/{Uri.EscapeDataString(article.Trim())}/{Uri.EscapeDataString(brand.Trim())}?filterByArticle", ct))
        .Parts ?? [];

    /// <summary>Бренды, под которыми у Nikei есть этот номер (пусто — номера нет).</summary>
    public async Task<List<string>> GetBrandsAsync(string article, CancellationToken ct) =>
        ((await GetAsync($"parts/search/{Uri.EscapeDataString(article.Trim())}?filterByArticle", ct)).Groups ?? [])
        .Select(x => x.Brand).OfType<string>().Where(x => x.Length > 0).ToList();

    private async Task<NikeiPartsResponse> GetAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authorization);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new NikeiApiException($"Nikei вернул HTTP {(int)response.StatusCode}.");

        var text = await response.Content.ReadAsStringAsync(ct);
        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(text, Json); }
        catch (JsonException) { throw new NikeiApiException("Nikei вернул невалидный JSON."); }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("status", out var status) &&
            status.ValueKind == JsonValueKind.String && string.Equals(status.GetString(), "error", StringComparison.OrdinalIgnoreCase))
        {
            var message = root.TryGetProperty("message", out var m) ? m.GetString() : root.TryGetProperty("text", out var t) ? t.GetString() : null;
            throw new NikeiApiException($"Nikei отклонил запрос. {message ?? "Запрос отклонён."}");
        }

        return root.ValueKind == JsonValueKind.Object ? root.Deserialize<NikeiPartsResponse>(Json) ?? new() : new();
    }
}
