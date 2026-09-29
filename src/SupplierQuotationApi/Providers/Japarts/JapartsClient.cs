using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Providers.Japarts;

public sealed class JapartsApiException(string message) : Exception(message);

/// <summary>Предложение Japarts (search3). Все поля приходят строками.</summary>
public sealed record JapartsOffer(string? PriceId, string? MakeName, string? DetailNum, string? DetailName, decimal? PriceRub,
    decimal? Quantity, decimal? Lot, int? DeliveryDays, int? GuaranteedDeliveryDays, string? Country, string? SupplierCode,
    int? Statistic, bool Deposit, bool UnconditionalReturn, bool Refurbished);

/// <summary>Клиент API Japarts: GET на корень сайта, действие в параметре action. Логин и пароль идут в URL,
/// поэтому логирование URL исходящих запросов отключено (System.Net.Http.HttpClient: Warning). Ответ — windows-1251.
/// «Ничего не найдено» — массив с одним объектом {"error":"NO RESULTS FOUND"}.</summary>
public sealed class JapartsClient
{
    private const string NotFound = "NO RESULTS FOUND";
    private static readonly Encoding Cp1251 = CreateEncoding();

    private readonly HttpClient _http;
    private readonly JapartsOptions _options;

    public JapartsClient(HttpClient http, IOptions<JapartsOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>У Japarts значения подставляются в SQL без экранирования: апостроф в номере или бренде даёт «Software error»
    /// с текстом запроса. Такие значения на сервер не отправляются.</summary>
    public static bool IsSafeParameter(string? value) => value is null || !value.Any(c => c is '\'' or '"' or '\\' or '`' or ';');

    /// <summary>Оригиналы (cross=0) по номеру и, если задан, бренду. Без бренда — все бренды номера.</summary>
    public async Task<List<JapartsOffer>> SearchAsync(string article, string? brand, CancellationToken ct)
    {
        if (!IsSafeParameter(article))
            throw new JapartsApiException("Japarts не принимает кавычки и спецсимволы в артикуле.");
        if (!IsSafeParameter(brand))
            throw new JapartsApiException("Бренд содержит символы, которые Japarts не принимает.");
        var query = new List<string>
        {
            "id=ws", "action=search3",
            $"login={Uri.EscapeDataString(_options.Login!)}", $"pass={Uri.EscapeDataString(_options.Password!)}",
            $"detailnum={Uri.EscapeDataString(article)}", "cross=0", "rowlimit=50"
        };
        if (!string.IsNullOrWhiteSpace(brand)) query.Add($"makename={Uri.EscapeDataString(brand)}");

        using var response = await _http.GetAsync($"?{string.Join("&", query)}", ct);
        if (!response.IsSuccessStatusCode)
            throw new JapartsApiException($"Japarts вернул HTTP {(int)response.StatusCode}.");

        var text = Cp1251.GetString(await response.Content.ReadAsByteArrayAsync(ct));
        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(text); }
        catch (JsonException) { throw new JapartsApiException("Japarts вернул некорректный JSON."); }

        // Ошибка приходит и объектом, и элементом массива.
        var error = ErrorOf(root);
        if (error is not null)
            return error == NotFound ? [] : throw new JapartsApiException($"Japarts: {error}");

        return root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(Parse).Where(x => !string.IsNullOrEmpty(x.PriceId)).ToList()
            : [];
    }

    private static string? ErrorOf(JsonElement root)
    {
        var candidate = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 ? root[0] : root;
        return candidate.ValueKind == JsonValueKind.Object && candidate.TryGetProperty("error", out var e)
            ? e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString()
            : null;
    }

    private static JapartsOffer Parse(JsonElement x) => new(
        Str(x, "priceid"), Str(x, "makename"), Str(x, "detailnum"), Str(x, "detailname"), Dec(x, "pricerur"), Dec(x, "quantity"),
        Dec(x, "lot"), Int(x, "time"), Int(x, "timegar"), Str(x, "country"), Str(x, "supcode"), Int(x, "statistic"),
        Str(x, "deposit") == "1", Str(x, "uncreturn") == "1", Str(x, "refurbished") == "1");

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())?.Trim()
            : null;

    private static decimal? Dec(JsonElement e, string name) =>
        decimal.TryParse(Str(e, name)?.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? Int(JsonElement e, string name) => int.TryParse(Str(e, name), out var value) ? value : null;

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251);
    }
}
