using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.FavoritParts;

/// <summary>Разворачивает ответ FavoritParts (товар → склады) в плоский список: один оффер на пару товар+склад.</summary>
public static class FavoritPartsMapper
{
    /// <summary>Сервис работает по Москве (UTC+3): срок считаем от московской даты, иначе вечером он завышается на сутки.</summary>
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    public static List<QuotationOffer> Map(IEnumerable<FavoritPartsGoods> goods, string requestedArticle, string? brand,
        bool includeAnalogues, DateTimeOffset now, ProducerAliasMap? aliases = null)
    {
        aliases ??= ProducerAliasMap.Empty;
        var offers = new List<QuotationOffer>();
        var requested = Normalize(requestedArticle);

        foreach (var item in goods)
        {
            if (Normalize(item.Number) == requested && aliases.Matches(item.Brand, brand))
                MapGoods(item, offers, isAnalogue: false, now);

            if (!includeAnalogues) continue;
            // У аналогов свой номер и бренд: фильтр по запрошенному артикулу к ним не применяется.
            foreach (var analogue in item.Analogues ?? [])
                MapGoods(analogue, offers, isAnalogue: true, now);
        }
        return offers;
    }

    private static void MapGoods(FavoritPartsGoods goods, List<QuotationOffer> offers, bool isAnalogue, DateTimeOffset now)
    {
        if (!Guid.TryParse(goods.GoodsId, out var goodsId) || goodsId == Guid.Empty) return;

        foreach (var warehouse in goods.Warehouses ?? [])
        {
            if (!Guid.TryParse(warehouse.Id, out var warehouseId) || warehouseId == Guid.Empty) continue;
            if (warehouse.Price is not { } price) continue;

            // Без остатка позиция приходит с нулём, но заказать её нельзя — в проценку не берём.
            var stock = warehouse.Stock ?? 0;
            if (stock <= 0) continue;

            var days = DeliveryDays(warehouse.ShipmentDate, now);
            var notRefund = warehouse.NotRefund ?? goods.NotRefund ?? false;

            offers.Add(new QuotationOffer
            {
                // Ключи FavoritParts принимает только в верхнем регистре.
                OfferId = $"{goodsId.ToString("D").ToUpperInvariant()}|{warehouseId.ToString("D").ToUpperInvariant()}",
                ProductName = goods.Name ?? goods.Number,
                Brand = goods.Brand,
                Article = goods.Number,
                Warehouse = string.IsNullOrWhiteSpace(warehouse.Code) ? null : warehouse.Code.Trim(),
                Stock = decimal.ToInt32(Math.Floor(stock)),
                StockText = stock.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                MinOrderQuantity = goods.Rate is > 0 ? goods.Rate.Value : 1,
                DeliveryDaysMin = days,
                DeliveryDaysMax = days,
                Price = new Money(price, "RUB"),
                PriceRub = new Money(price, "RUB"),
                PriceNote = notRefund ? "Возврат не разрешён" : null,
                ProviderData = new Dictionary<string, string?>
                {
                    ["goodsId"] = goodsId.ToString("D").ToUpperInvariant(),
                    ["warehouseGroupId"] = warehouseId.ToString("D").ToUpperInvariant(),
                    ["warehouseShipping"] = warehouse.WarehouseShipping,
                    ["shipmentAt"] = warehouse.ShipmentDate?.ToString("o"),
                    ["isAnalog"] = isAnalogue ? "true" : "false",
                    ["ownWarehouse"] = warehouse.Own?.ToString().ToLowerInvariant(),
                    ["notRefund"] = notRefund ? "true" : "false"
                }
            });
        }
    }

    /// <summary>Дней до отгрузки по московским датам. null-дата — 0.</summary>
    public static int DeliveryDays(DateTimeOffset? shipment, DateTimeOffset now)
    {
        if (shipment is null) return 0;
        var today = DateOnly.FromDateTime(now.ToOffset(MoscowOffset).DateTime);
        var shipDay = DateOnly.FromDateTime(shipment.Value.ToOffset(MoscowOffset).DateTime);
        return Math.Max(0, shipDay.DayNumber - today.DayNumber);
    }

    /// <summary>Артикул без разделителей и регистра: 7160-500 039 S == 7160500039S.</summary>
    public static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
