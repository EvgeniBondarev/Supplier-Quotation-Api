using System.Text.Json.Serialization;

namespace SupplierQuotationApi.Providers.FavoritParts;

public sealed class FavoritPartsGoods
{
    [JsonPropertyName("goodsID")] public string? GoodsId { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>Кратность заказа.</summary>
    [JsonPropertyName("rate")] public int? Rate { get; set; }
    [JsonPropertyName("notRefund")] public bool? NotRefund { get; set; }
    [JsonPropertyName("warehouses")] public List<FavoritPartsWarehouse>? Warehouses { get; set; }
    /// <summary>Аналоги: та же структура, что и у товара.</summary>
    [JsonPropertyName("analogues")] public List<FavoritPartsGoods>? Analogues { get; set; }
}

public sealed class FavoritPartsWarehouse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("price")] public decimal? Price { get; set; }
    [JsonPropertyName("stock")] public decimal? Stock { get; set; }
    [JsonPropertyName("shipmentDate")] public DateTimeOffset? ShipmentDate { get; set; }
    [JsonPropertyName("notRefund")] public bool? NotRefund { get; set; }
    [JsonPropertyName("own")] public bool? Own { get; set; }
    [JsonPropertyName("warehouseShipping")] public string? WarehouseShipping { get; set; }
}
