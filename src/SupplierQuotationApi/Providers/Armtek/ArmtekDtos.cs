using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupplierQuotationApi.Providers.Armtek;

/// <summary>Числовые поля Armtek приходят то числом, то строкой — приводим к строке.</summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => null
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

public sealed record ArmtekMessage(
    [property: JsonPropertyName("TYPE")] string? Type,
    [property: JsonPropertyName("TEXT")] string? Text);

public sealed class ArmtekEnvelope
{
    [JsonPropertyName("STATUS")] public int Status { get; set; }
    [JsonPropertyName("MESSAGES")] public List<ArmtekMessage>? Messages { get; set; }
    [JsonPropertyName("RESP")] public JsonElement Resp { get; set; }
}

public sealed class ArmtekSearchItem
{
    [JsonPropertyName("ARTID"), JsonConverter(typeof(FlexibleStringConverter))] public string? ArtId { get; set; }
    [JsonPropertyName("PARNR"), JsonConverter(typeof(FlexibleStringConverter))] public string? PartnerWarehouseCode { get; set; }
    [JsonPropertyName("KEYZAK")] public string? Keyzak { get; set; }
    [JsonPropertyName("PIN")] public string? Pin { get; set; }
    [JsonPropertyName("BRAND")] public string? Brand { get; set; }
    [JsonPropertyName("NAME")] public string? Name { get; set; }
    [JsonPropertyName("RVALUE"), JsonConverter(typeof(FlexibleStringConverter))] public string? Rvalue { get; set; }
    [JsonPropertyName("RETDAYS"), JsonConverter(typeof(FlexibleStringConverter))] public string? RetDays { get; set; }
    [JsonPropertyName("RDPRF"), JsonConverter(typeof(FlexibleStringConverter))] public string? Multiplicity { get; set; }
    [JsonPropertyName("MINBM"), JsonConverter(typeof(FlexibleStringConverter))] public string? MinQuantity { get; set; }
    [JsonPropertyName("VENSL"), JsonConverter(typeof(FlexibleStringConverter))] public string? Probability { get; set; }
    [JsonPropertyName("PRICE"), JsonConverter(typeof(FlexibleStringConverter))] public string? Price { get; set; }
    [JsonPropertyName("WAERS")] public string? Currency { get; set; }
    [JsonPropertyName("DLVDT"), JsonConverter(typeof(FlexibleStringConverter))] public string? DeliveryDate { get; set; }
    [JsonPropertyName("WRNTDT"), JsonConverter(typeof(FlexibleStringConverter))] public string? GuaranteedDeliveryDate { get; set; }
    /// <summary>"X" — аналог, а не запрошенный PIN/BRAND.</summary>
    [JsonPropertyName("ANALOG")] public string? Analog { get; set; }
    [JsonPropertyName("TYPEB")] public string? TypeB { get; set; }
    [JsonPropertyName("DSPEC")] public string? Dspec { get; set; }
    [JsonPropertyName("RCOST"), JsonConverter(typeof(FlexibleStringConverter))] public string? MaxRetailPrice { get; set; }
    [JsonPropertyName("PNOTE")] public string? Note { get; set; }
}

public sealed class ArmtekStoreItem
{
    [JsonPropertyName("KEYZAK")] public string? Keyzak { get; set; }
    [JsonPropertyName("SKLNAME")] public string? SklName { get; set; }
}

public sealed class ArmtekAssortmentItem
{
    [JsonPropertyName("PIN")] public string? Pin { get; set; }
    [JsonPropertyName("BRAND")] public string? Brand { get; set; }
}

public sealed class ArmtekUserInfo
{
    [JsonPropertyName("STRUCTURE")] public ArmtekUserStructure? Structure { get; set; }
}

public sealed class ArmtekUserStructure
{
    [JsonPropertyName("RG_TAB")] public List<ArmtekBuyer>? Buyers { get; set; }
}

public sealed class ArmtekBuyer
{
    [JsonPropertyName("KUNNR")] public string? Kunnr { get; set; }
    [JsonPropertyName("DEFAULT"), JsonConverter(typeof(FlexibleStringConverter))] public string? Default { get; set; }
}
