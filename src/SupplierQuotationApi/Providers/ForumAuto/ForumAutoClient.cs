using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupplierQuotationApi.Providers.ForumAuto;

public sealed class ForumAutoApiException(string message) : Exception(message);

public sealed class ForumAutoRow
{
    [JsonPropertyName("gid")] public string? Gid { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("art")] public string? Art { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>Срок доставки, дни.</summary>
    [JsonPropertyName("d_deliv")] public int? DeliveryDays { get; set; }
    /// <summary>Срок доставки, всего часов.</summary>
    [JsonPropertyName("h_deliv")] public int? DeliveryHours { get; set; }
    /// <summary>Кратность заказа.</summary>
    [JsonPropertyName("kr")] public int? Multiplicity { get; set; }
    [JsonPropertyName("num")] public int? Stock { get; set; }
    [JsonPropertyName("price")] public decimal? Price { get; set; }
    [JsonPropertyName("whse")] public string? Warehouse { get; set; }
    [JsonPropertyName("is_returnable")] public int? IsReturnable { get; set; }
}

/// <summary>Клиент read-only API Forum-Auto v2 (listgoods). Авторизация — query login/pass.
/// Ошибки приходят в теле как {"errors": {FaultCode, FaultString}} при HTTP 200.</summary>
public sealed class ForumAutoClient
{
    /// <summary>FaultCode 27 — «Товары не найдены»: нормальный пустой результат.</summary>
    public const int NotFoundFaultCode = 27;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly ForumAutoOptions _options;

    public ForumAutoClient(HttpClient http, ForumAutoOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<List<ForumAutoRow>> ListGoodsAsync(string article, bool cross, CancellationToken cancellationToken)
    {
        var query = $"login={Uri.EscapeDataString(_options.Login!)}&pass={Uri.EscapeDataString(_options.Password!)}" +
                    $"&art={Uri.EscapeDataString(article)}&cross={(cross ? 1 : 0)}";
        using var response = await _http.GetAsync($"listgoods?{query}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ForumAutoApiException($"Forum-Auto вернул HTTP {(int)response.StatusCode}.");

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(text, Json); }
        catch (JsonException) { throw new ForumAutoApiException("Forum-Auto вернул невалидный JSON."); }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Object)
        {
            var code = errors.TryGetProperty("FaultCode", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : (int?)null;
            if (code == NotFoundFaultCode) return [];

            var message = errors.TryGetProperty("FaultString", out var s) ? s.GetString() : null;
            throw new ForumAutoApiException(string.IsNullOrWhiteSpace(message) ? "Forum-Auto вернул ошибку." : message);
        }

        return root.ValueKind == JsonValueKind.Array ? root.Deserialize<List<ForumAutoRow>>(Json) ?? [] : [];
    }
}
