using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Providers.Berg;

public sealed class BergApiException(string message) : Exception(message);

/// <summary>Клиент API Berg v1.0. Ключ — в заголовке X-Berg-API-Key, в URL его нет.
/// Ошибки Berg отдаёт телом {"errors":[…]} вместе с кодом 4xx: тело разбирается раньше статуса.</summary>
public sealed class BergClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly BergOptions _options;

    public BergClient(HttpClient http, IOptions<BergOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<List<BergResource>> GetStockAsync(string article, bool analogs, long? addressId,
        CancellationToken cancellationToken)
    {
        var query = $"items[0][resource_article]={Uri.EscapeDataString(article)}&analogs={(analogs ? 1 : 0)}";
        if (addressId is not null) query += $"&address_id={addressId}";

        var response = await GetAsync<BergStockResponse>($"v1.0/ordering/get_stock.json?{query}", cancellationToken);
        return response?.Resources ?? [];
    }

    /// <summary>Активные адреса отгрузки аккаунта.</summary>
    public async Task<List<BergAddress>> GetActiveAddressesAsync(CancellationToken cancellationToken) =>
        (await GetAsync<BergAddressList>("v1.0/references/shipment_address/active.json", cancellationToken))?.Addresses ?? [];

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("X-Berg-API-Key", _options.ApiKey);

        using var response = await _http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        JsonElement root = default;
        var parsed = false;
        try { root = JsonSerializer.Deserialize<JsonElement>(text, Json); parsed = true; }
        catch (JsonException) { }

        if (parsed && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors))
            throw new BergApiException(DescribeErrors(errors));
        if (!response.IsSuccessStatusCode)
            throw new BergApiException($"Berg вернул HTTP {(int)response.StatusCode}.");
        if (!parsed) throw new BergApiException("Berg вернул невалидный JSON.");

        return root.Deserialize<T>(Json);
    }

    private static string DescribeErrors(JsonElement errors)
    {
        var parts = new List<string>();
        if (errors.ValueKind == JsonValueKind.Array)
            foreach (var e in errors.EnumerateArray())
                parts.Add(e.ValueKind == JsonValueKind.String ? e.GetString()!
                    : e.TryGetProperty("message", out var m) ? m.GetString() ?? e.ToString()
                    : e.TryGetProperty("code", out var c) ? c.ToString() : e.ToString());
        else parts.Add(errors.ToString());
        return $"Berg: {string.Join("; ", parts.Where(x => !string.IsNullOrWhiteSpace(x)))}";
    }
}
