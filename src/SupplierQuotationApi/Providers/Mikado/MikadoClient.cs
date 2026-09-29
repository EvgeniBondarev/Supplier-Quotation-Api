using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Providers.Mikado;

public sealed class MikadoApiException(string message) : Exception(message);

/// <summary>Строка CodeBrandLine: предложение одного склада.</summary>
public sealed record MikadoLine(string? OrderCode, string? Brand, string? Name, string? PriceRur, string? StockId, string? StockName,
    string? StockQty, string? MinOrderQty, string? DeliveryDelay, string? StockDelay, string? CertificateUrl);

/// <summary>SOAP 1.1 клиент Микадо (service.asmx). ASMX разбирает тело строгой последовательностью полей, а namespace
/// у каждого endpoint свой: при неверном сервис не возвращает ошибку транспорта, а отвечает Message = «Некорректное значение
/// параметра [ClientID]» с пустым списком — поэтому Message проверяется всегда, пустой список сам по себе «нет данных» не значит.</summary>
public sealed class MikadoClient
{
    private const string ServiceNamespace = "http://mikado-parts.ru/service";
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Service = ServiceNamespace;

    private readonly HttpClient _http;
    private readonly MikadoOptions _options;

    public MikadoClient(HttpClient http, IOptions<MikadoOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    /// <summary>Остатки и цены по коду и бренду (оба обязательны). Поле «Code» принимает и «SF-2364», и «SF2364».</summary>
    public async Task<List<MikadoLine>> CodeBrandStockInfoAsync(string code, string brand, CancellationToken ct)
    {
        // Порядок полей значим: Code, Brand, ClientID, Password.
        var operation = new XElement(Service + "CodeBrandStockInfo",
            new XElement(Service + "Code", code),
            new XElement(Service + "Brand", brand),
            new XElement(Service + "ClientID", _options.ClientId),
            new XElement(Service + "Password", _options.Password));
        var envelope = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement(Soap + "Envelope", new XElement(Soap + "Body", operation)));

        using var request = new HttpRequestMessage(HttpMethod.Post, "service.asmx")
        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml")
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{ServiceNamespace}/CodeBrandStockInfo\"");

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new MikadoApiException($"Микадо вернул HTTP {(int)response.StatusCode}.");

        XDocument xml;
        try { xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (System.Xml.XmlException) { throw new MikadoApiException("Микадо вернул невалидный XML."); }

        var fault = xml.Descendants().FirstOrDefault(x => x.Name.LocalName == "faultstring");
        if (fault is not null) throw new MikadoApiException($"Микадо: {fault.Value}");

        var result = xml.Descendants().FirstOrDefault(x => x.Name.LocalName == "CodeBrandStockInfoResult")
                     ?? throw new MikadoApiException("Микадо вернул пустой ответ.");
        var message = Value(result, "Message");
        if (!string.Equals(message, "Ok", StringComparison.OrdinalIgnoreCase))
            throw new MikadoApiException($"Микадо: {message ?? "ответ без сообщения"}");

        var certificateUrl = result.Descendants().FirstOrDefault(x => x.Name.LocalName == "Url")?.Value.Trim();
        return result.Descendants().Where(x => x.Name.LocalName == "CodeBrandLine").Select(line => new MikadoLine(
            Value(line, "OrderCode"), Value(line, "Brand"), Value(line, "Name"), Value(line, "PriceRUR"), Value(line, "StokID"),
            Value(line, "StokName"), Value(line, "StockQTY"), Value(line, "MinZakazQTY"), Value(line, "DeliveryDelay"),
            Value(line, "StockDelay"), string.IsNullOrEmpty(certificateUrl) ? null : certificateUrl)).ToList();
    }

    private static string? Value(XElement parent, string name)
    {
        var value = parent.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
