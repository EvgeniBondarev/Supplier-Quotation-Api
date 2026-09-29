using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Providers.ProfitLiga;

public sealed class ProfitLigaApiException(string message) : Exception(message);

/// <summary>Карточка товара с предложениями складов.</summary>
public sealed record ProfitLigaCard(string? Article, string? Brand, string? Description, IReadOnlyList<ProfitLigaOffer> Offers);

/// <summary>Предложение склада. Ключ словаря products — устойчивый хеш строки на стороне поставщика.</summary>
public sealed record ProfitLigaOffer(string? Key, string? ArticleId, string? WarehouseId, string? ProductCode, string? Article,
    string? Brand, string? Description, int? Multi, decimal? Quantity, decimal? Price, string? WarehouseName, int? DeliveryHours,
    string? DeliveryDate, bool AllowReturn, int? ReturnDays, decimal? Probability, decimal? Waitings, string? Comment, bool Sale);

/// <summary>Клиент API Профит-Лиги v1.4. Ключ идёт GET-параметром secret даже в POST, поэтому логирование
/// URL исходящих запросов отключено в настройках (System.Net.Http.HttpClient: Warning).
/// PHP отдаёт пустую коллекцию массивом, а непустую — объектом: обе формы разбираются одинаково.
/// Ошибка приходит с HTTP 200 и полем status = error.</summary>
public sealed class ProfitLigaClient
{
    private readonly HttpClient _http;
    private readonly ProfitLigaOptions _options;

    public ProfitLigaClient(HttpClient http, IOptions<ProfitLigaOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>Единственный метод с фильтром бренда на стороне API. replaces=0 — только оригиналы, без замен.</summary>
    public Task<List<ProfitLigaCard>> SearchCrossesAsync(string article, string brand, CancellationToken ct) =>
        GetAsync($"search/crosses?article={Uri.EscapeDataString(article)}&brand={Uri.EscapeDataString(brand)}&replaces=0", ct);

    /// <summary>Бренд неизвестен: отдаёт все бренды артикула, у которых есть наличие.</summary>
    public Task<List<ProfitLigaCard>> SearchItemsAsync(string article, CancellationToken ct) =>
        GetAsync($"search/items?article={Uri.EscapeDataString(article)}", ct);

    private async Task<List<ProfitLigaCard>> GetAsync(string pathAndQuery, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"{pathAndQuery}&secret={Uri.EscapeDataString(_options.Secret!)}", ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new ProfitLigaApiException($"Профит-Лига вернула HTTP {(int)response.StatusCode}.");

        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(text); }
        catch (JsonException) { throw new ProfitLigaApiException("Профит-Лига вернула невалидный JSON."); }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("status", out var status) &&
            status.ValueKind == JsonValueKind.String && status.GetString() == "error")
            throw new ProfitLigaApiException($"Профит-Лига отклонила запрос. {ErrorText(root)}".TrimEnd());

        return Items(root).Select(ParseCard).ToList();
    }

    private static ProfitLigaCard ParseCard(JsonElement card) => new(
        Str(card, "article"), Str(card, "brand"), Str(card, "description"),
        KeyedItems(card, "products").Select(x => ParseOffer(x.Key, x.Value)).ToList());

    private static ProfitLigaOffer ParseOffer(string? key, JsonElement o) => new(
        key, Str(o, "article_id"), Str(o, "warehouse_id"), Str(o, "product_code"), Str(o, "article"), Str(o, "brand"),
        Str(o, "description"), Int(o, "multi"), Dec(o, "quantity"), Dec(o, "price"), Str(o, "custom_warehouse_name"),
        Int(o, "delivery_time"), Str(o, "delivery_date"), Flag(o, "allow_return"), Int(o, "return_days"),
        Dec(o, "delivery_probability"), Dec(o, "waitings"), Str(o, "comment"), Flag(o, "sale"));

    /// <summary>Объекты из массива или из словаря (значения).</summary>
    private static IEnumerable<JsonElement> Items(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => e.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object),
        JsonValueKind.Object => e.EnumerateObject().Select(p => p.Value).Where(x => x.ValueKind == JsonValueKind.Object),
        _ => []
    };

    private static IEnumerable<(string? Key, JsonElement Value)> KeyedItems(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var e)) return [];
        return e.ValueKind switch
        {
            JsonValueKind.Array => e.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => ((string?)null, x)),
            JsonValueKind.Object => e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object).Select(p => ((string?)p.Name, p.Value)),
            _ => []
        };
    }

    private static string ErrorText(JsonElement root) =>
        root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()!
        : root.TryGetProperty("error", out var e) ? e.ToString() : string.Empty;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())?.Trim()
            : null;

    /// <summary>Числа приходят и числом, и строкой; «&gt;10» в остатке означает «не менее».</summary>
    private static decimal? Dec(JsonElement e, string name) =>
        decimal.TryParse(Str(e, name)?.Trim('>', '<', '~', '='), System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? Int(JsonElement e, string name) => Dec(e, name) is { } d ? decimal.ToInt32(d) : null;

    /// <summary>Флаг приходит как true / "1" / 1.</summary>
    private static bool Flag(JsonElement e, string name) => Str(e, name) is "1" or "true" or "True";
}
