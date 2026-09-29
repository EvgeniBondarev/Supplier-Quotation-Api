using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupplierQuotationApi.Providers.Motex;

/// <summary>Числа приходят то числом, то строкой с суффиксом («20>» = «от 20 шт.») — читаем как строку.</summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

public sealed class MotexArticlesResponse
{
    [JsonPropertyName("articles")] public List<MotexArticle>? Articles { get; set; }
}

public sealed class MotexArticle
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("price"), JsonConverter(typeof(FlexibleStringConverter))] public string? Price { get; set; }
    [JsonPropertyName("quantity"), JsonConverter(typeof(FlexibleStringConverter))] public string? Quantity { get; set; }
    [JsonPropertyName("multiple"), JsonConverter(typeof(FlexibleStringConverter))] public string? Multiple { get; set; }
    [JsonPropertyName("storeCode"), JsonConverter(typeof(FlexibleStringConverter))] public string? StoreCode { get; set; }
    [JsonPropertyName("storeInfo")] public string? StoreInfo { get; set; }
    /// <summary>yyyyMMddHHmmss, время Минска (UTC+3).</summary>
    [JsonPropertyName("deliveryDate"), JsonConverter(typeof(FlexibleStringConverter))] public string? DeliveryDate { get; set; }
    [JsonPropertyName("returnPeriod")] public int? ReturnPeriod { get; set; }
    [JsonPropertyName("isAnalog")] public int? IsAnalog { get; set; }
    /// <summary>1 — постановление 713 (ограничение надбавки).</summary>
    [JsonPropertyName("resolutionTag")] public int? ResolutionTag { get; set; }
    [JsonPropertyName("remainingMarkup"), JsonConverter(typeof(FlexibleStringConverter))] public string? RemainingMarkup { get; set; }
}

public sealed class MotexAddressesResponse
{
    [JsonPropertyName("deliveryAddresses")] public List<MotexAddress>? DeliveryAddresses { get; set; }
}

public sealed class MotexAddress
{
    [JsonPropertyName("code"), JsonConverter(typeof(FlexibleStringConverter))] public string? Code { get; set; }
}

public sealed class MotexLoginResponse
{
    [JsonPropertyName("token")] public string? Token { get; set; }
    [JsonPropertyName("expireIn")] public int? ExpireIn { get; set; }
}
