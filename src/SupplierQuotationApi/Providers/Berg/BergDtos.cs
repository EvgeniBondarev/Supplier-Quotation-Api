using System.Text.Json.Serialization;

namespace SupplierQuotationApi.Providers.Berg;

public sealed class BergStockResponse
{
    [JsonPropertyName("resources")] public List<BergResource>? Resources { get; set; }
}

public sealed class BergResource
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("article")] public string? Article { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("brand")] public BergBrand? Brand { get; set; }
    [JsonPropertyName("offers")] public List<BergOffer>? Offers { get; set; }
}

public sealed class BergBrand
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public sealed class BergOffer
{
    [JsonPropertyName("warehouse")] public BergWarehouse? Warehouse { get; set; }
    [JsonPropertyName("price")] public decimal? Price { get; set; }
    [JsonPropertyName("average_period")] public int? AveragePeriod { get; set; }
    [JsonPropertyName("assured_period")] public int? AssuredPeriod { get; set; }
    [JsonPropertyName("reliability")] public int? Reliability { get; set; }
    [JsonPropertyName("is_transit")] public bool? IsTransit { get; set; }
    [JsonPropertyName("quantity")] public decimal? Quantity { get; set; }
    [JsonPropertyName("available_more")] public bool? AvailableMore { get; set; }
    [JsonPropertyName("multiplication_factor")] public int? MultiplicationFactor { get; set; }
    [JsonPropertyName("address_timetable")] public List<BergTimetable>? AddressTimetable { get; set; }
}

public sealed class BergWarehouse
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

/// <summary>Слот доставки. Даты без пояса — по времени Москвы.</summary>
public sealed class BergTimetable
{
    // Формат «2026-10-01 10:00:00» (с пробелом, не ISO): читаем строкой и разбираем сами.
    [JsonPropertyName("delivery_from")] public string? DeliveryFrom { get; set; }
    [JsonPropertyName("delivery_to")] public string? DeliveryTo { get; set; }
}

public sealed class BergAddressList
{
    [JsonPropertyName("shipment_address_list")] public List<BergAddress>? Addresses { get; set; }
}

public sealed class BergAddress
{
    [JsonPropertyName("id")] public long? Id { get; set; }
    [JsonPropertyName("state")] public int? State { get; set; }
}
