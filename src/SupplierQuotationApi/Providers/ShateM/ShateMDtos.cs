namespace SupplierQuotationApi.Providers.ShateM;

// Только используемые поля ответа API Шате-М; остальное игнорируется.
public sealed record ShateMArticle(int Id, string? Code, string? TradeMarkName, string? Name, string? Description);

public sealed record ShateMArticleRef(ShateMArticle Article);

public sealed record ShateMArticlePrices(ShateMArticle Article, List<ShateMPrice>? Prices);

public sealed record ShateMPrice(
    string? Id,
    int ArticleId,
    string? LocationCode,
    string? LocationCodeReal,
    string? AgreementCode,
    ShateMPriceValue? Price,
    ShateMQuantity? Quantity,
    ShateMAddInfo? AddInfo,
    long? Hash,
    List<ShateMDelivery>? DeliveryDateTimes,
    DateTimeOffset? ShippingDateTime,
    ShateMSupplyProbability? SupplyProbability);

public sealed record ShateMPriceValue(decimal? Value, string? CurrencyCode);

public sealed record ShateMQuantity(decimal? Available, string? AvailableType, decimal? Multiplicity, decimal? Minimum);

public sealed record ShateMAddInfo(string? WarningText, string? Comment);

public sealed record ShateMDelivery(DateTimeOffset? DeliveryDateTime);

public sealed record ShateMSupplyProbability(int? Rating);

public sealed record ShateMLocation(string? Code, string? Name, string? City);

public sealed record ShateMToken(
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string? AccessToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int? ExpiresIn);
