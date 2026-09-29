using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Providers.Moskvorechie;

/// <summary>JSON API портала (price_by_nr_firm). Логин и ключ идут в теле POST, не в URL.
/// API игнорирует фирму и может вернуть другой бренд при том же номере — поэтому используется только без бренда.</summary>
public sealed class MoskvorechieApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly MoskvorechieOptions _options;

    public MoskvorechieApiClient(HttpClient http, IOptions<MoskvorechieOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<List<MoskvorechieApiRow>> SearchPricesAsync(string article, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["l"] = _options.Login!, ["p"] = _options.ApiKey!, ["cs"] = "utf8", ["act"] = "price_by_nr_firm", ["nr"] = article
        };
        using var response = await _http.PostAsync(_options.BaseUrl!.Trim(), new FormUrlEncodedContent(form), ct);
        if (!response.IsSuccessStatusCode)
            throw new MoskvorechieApiException($"Москворечье вернул HTTP {(int)response.StatusCode}.");

        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(ct), Json); }
        catch (JsonException) { throw new MoskvorechieApiException("Москворечье вернул невалидный JSON."); }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
            throw new MoskvorechieApiException(error.ToString());

        var rows = root.ValueKind == JsonValueKind.Array ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Array ? r
            : default;
        return rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray().Select(Parse).ToList() : [];
    }

    private static MoskvorechieApiRow Parse(JsonElement e) => new(
        Text(e, "nr"), Text(e, "brand"), Text(e, "name"), Text(e, "gid") ?? Text(e, "id"), Text(e, "delivery"),
        Number(e, "price"), (int?)Number(e, "stock") ?? 0, (int?)Number(e, "sorder") ?? 0, (int?)Number(e, "minq") ?? 1);

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : v.ToString() : null;

    private static decimal? Number(JsonElement e, string name) =>
        decimal.TryParse(Text(e, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
}

public sealed record MoskvorechieApiRow(string? Article, string? Brand, string? Name, string? Gid, string? Delivery,
    decimal? Price, int Stock, int OrderStock, int MinQuantity);
