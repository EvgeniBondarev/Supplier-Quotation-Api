using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Providers.Avd;

public sealed class AvdApiException(string message) : Exception(message);

/// <summary>Одно ценовое предложение АВД (PriceItem3). АВД — агрегатор: у каждого предложения свой поставщик и регион.</summary>
public sealed class AvdOffer
{
    public string? CatalogName { get; init; }
    public string? ItemName { get; init; }
    public string? ItemNumber { get; init; }
    public decimal? Price { get; init; }
    public decimal? Quantity { get; init; }
    public int? Multiply { get; init; }
    public int? SupplierPeriod { get; init; }
    public string? PriceAverage { get; init; }
    public string? PriceStatistic { get; init; }
    public string? SupplierName { get; init; }
    public string? SupplierRegion { get; init; }
    public string? SupplierInfo { get; init; }
    public string? DealerStore { get; init; }
    public string? DatePrice { get; init; }
    public bool? IsOriginal { get; init; }
    public string? SupplierReturn { get; init; }
    public string? SupplierReturnDescription { get; init; }
    public string? SupplierPrepay { get; init; }
    /// <summary>Хэш предложения: обязателен для InsertToBasket.</summary>
    public string? Hash { get; init; }
}

/// <summary>SOAP 1.1 клиент АВД. Логин и пароль идут в теле запроса, в URL их нет.</summary>
public sealed class AvdClient
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Service = "http://tempuri.org/";

    private readonly HttpClient _http;
    private readonly AvdOptions _options;

    public AvdClient(HttpClient http, IOptions<AvdOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>Каталоги (бренды), в которых есть номер.</summary>
    public async Task<List<string>> GetCatalogsAsync(string number, CancellationToken cancellationToken)
    {
        var xml = await SendAsync("GetCatalogsList", new() { ["number"] = number }, cancellationToken);
        return xml.Descendants().Where(x => x.Name.LocalName == "CatalogName")
            .Select(x => x.Value.Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Оригинальные предложения номера в каталоге (кроссы — отдельный метод GetFastCrossesPrice).</summary>
    public async Task<List<AvdOffer>> GetOriginalPriceAsync(string number, string catalog, CancellationToken cancellationToken)
    {
        var xml = await SendAsync("GetOriginalPrice",
            new() { ["number"] = number, ["catalog"] = catalog, ["supplier"] = string.Empty }, cancellationToken);
        return xml.Descendants().Where(x => x.Name.LocalName == "PriceItem3").Select(ParseOffer)
            .Where(x => x.Price.HasValue).ToList();
    }

    private async Task<XElement> SendAsync(string operation, Dictionary<string, string> parameters, CancellationToken ct)
    {
        var element = new XElement(Service + operation,
            new XElement(Service + "login", _options.Login),
            new XElement(Service + "password", _options.Password));
        foreach (var (name, value) in parameters) element.Add(new XElement(Service + name, value));

        var envelope = new XDocument(new XElement(Soap + "Envelope", new XElement(Soap + "Body", element)));
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl!.Trim()) // абсолютный адрес: слэш в конце ломает WCF-endpoint

        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml")
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"http://tempuri.org/IAvdUserService/{operation}");

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new AvdApiException($"АВД вернул HTTP {(int)response.StatusCode}.");

        XDocument xml;
        try { xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (System.Xml.XmlException) { throw new AvdApiException("АВД вернул невалидный XML."); }

        var fault = xml.Descendants(Soap + "Fault").FirstOrDefault();
        if (fault is not null)
            throw new AvdApiException($"АВД вернул SOAP Fault: {fault.Element("faultstring")?.Value ?? "без описания"}.");

        return xml.Root ?? throw new AvdApiException("АВД вернул пустой SOAP-ответ.");
    }

    private static AvdOffer ParseOffer(XElement x) => new()
    {
        CatalogName = Text(x, "CatalogName"),
        ItemName = Text(x, "ItemName"),
        ItemNumber = Text(x, "ItemNumber"),
        Price = Decimal(x, "Price"),
        Quantity = Decimal(x, "Quantity"),
        Multiply = Decimal(x, "Multiply") is { } m ? decimal.ToInt32(m) : null,
        SupplierPeriod = Decimal(x, "SupplierPeriod") is { } p ? decimal.ToInt32(p) : null,
        PriceAverage = Text(x, "PriceAverage"),
        PriceStatistic = Text(x, "PriceStatistic"),
        SupplierName = Text(x, "SupplierName"),
        SupplierRegion = Text(x, "SupplierRegion"),
        SupplierInfo = Text(x, "SupplierInfo"),
        DealerStore = Text(x, "DealerStore"),
        DatePrice = Text(x, "DatePrice"),
        IsOriginal = bool.TryParse(Text(x, "IsOriginal"), out var original) ? original : null,
        SupplierReturn = Text(x, "SupplierReturn"),
        SupplierReturnDescription = Text(x, "SupplierReturn_description"),
        SupplierPrepay = Text(x, "SupplierPrepay"),
        Hash = Text(x, "Hash")
    };

    private static string? Text(XElement parent, string name)
    {
        var value = parent.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static decimal? Decimal(XElement parent, string name) =>
        decimal.TryParse(Text(parent, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
}
