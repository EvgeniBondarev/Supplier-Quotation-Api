using System.Text.Json;

namespace SupplierQuotationApi.Providers.FavoritParts;

public sealed class FavoritPartsApiException(string message) : Exception(message);

/// <summary>Клиент проценки FavoritParts (hs/hsprice). Авторизация — query key/developerKey.
/// Ошибки сервис отдаёт как {"error": "..."} при HTTP 200.</summary>
public sealed class FavoritPartsClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly FavoritPartsOptions _options;

    public FavoritPartsClient(HttpClient http, FavoritPartsOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<List<FavoritPartsGoods>> SearchAsync(string number, string? brand, bool analogues,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"key={Uri.EscapeDataString(_options.ClientKey!)}",
            $"number={Uri.EscapeDataString(number)}",
            "info=on"
        };
        if (!string.IsNullOrWhiteSpace(_options.DeveloperKey)) query.Add($"developerKey={Uri.EscapeDataString(_options.DeveloperKey)}");
        if (!string.IsNullOrWhiteSpace(brand)) query.Add($"brand={Uri.EscapeDataString(brand)}");
        if (analogues) query.Add("analogues=on");

        using var response = await _http.GetAsync($"hs/hsprice/?{string.Join("&", query)}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new FavoritPartsApiException($"FavoritParts вернул HTTP {(int)response.StatusCode}.");

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(text, Json); }
        catch (JsonException) { throw new FavoritPartsApiException("FavoritParts вернул невалидный JSON."); }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.String)
            throw new FavoritPartsApiException(error.GetString() ?? "FavoritParts вернул ошибку.");

        var goods = root.ValueKind switch
        {
            JsonValueKind.Array => root,
            JsonValueKind.Object when root.TryGetProperty("goods", out var g) && g.ValueKind == JsonValueKind.Array => g,
            _ => default
        };
        return goods.ValueKind == JsonValueKind.Array
            ? goods.Deserialize<List<FavoritPartsGoods>>(Json) ?? []
            : [];
    }
}
