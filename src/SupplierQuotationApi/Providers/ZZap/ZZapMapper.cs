using System.Globalization;
using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.ZZap;

/// <summary>Преобразует ответ /search/light в единые офферы. ZZap — агрегатор: строка — продавец, а не склад.</summary>
public static class ZZapMapper
{
    /// <summary>Правила ZZap требуют явно указывать источник данных при их показе.</summary>
    public const string Attribution = "Информация о запчастях предоставлена системой ZZap";

    /// <summary>delivery_days ≥ порога ZZap использует как «срок не указан» (254/255): реальным сроком это показывать нельзя.</summary>
    public const int UnknownDeliveryDaysThreshold = 100;

    /// <summary>Сколько дешёвых предложений отдаём: по ходовому артикулу ZZap возвращает сотню продавцов.</summary>
    public const int MaxOffers = 20;

    private const int UnlimitedQtyMax = 999_999_999;

    public static List<QuotationOffer> MapOriginals(IEnumerable<ZZapOffer> offers, string requestedArticle, int codeRegion)
    {
        var wanted = Normalize(requestedArticle);
        var result = new List<QuotationOffer>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var offer in offers)
        {
            // Номер сверяем сами: ZZap отдаёт «OC 90» на запрос «OC90», а /search/light по контракту не возвращает замены,
            // поэтому чужой номер здесь — шум. Бренд не сверяем: class_man в ответе пляшет («MAHLE», «MAHLE/KNECHT»).
            if (Normalize(offer.PartNumber) != wanted) continue;
            if (Map(offer, requestedArticle, codeRegion) is not { } mapped || !seen.Add(mapped.OfferId!)) continue;
            result.Add(mapped);
        }

        return result
            .OrderBy(x => x.Price.Amount)
            .ThenBy(x => x.DeliveryDaysMin)
            .Take(MaxOffers)
            .ToList();
    }

    private static QuotationOffer? Map(ZZapOffer offer, string article, int codeRegion)
    {
        if (offer.Price is not { } price || price <= 0m) return null;
        if (string.IsNullOrWhiteSpace(offer.CodeDocB)) return null;
        // Б/у отсеяно type_request=5, но страхуемся: в проценку идут только новые детали.
        if (offer.Used == true) return null;

        var days = offer.DeliveryDays ?? 0;
        if (days >= UnknownDeliveryDaysThreshold) return null;

        var pack = Math.Max(1, offer.Pack ?? 1);
        var qtyMax = offer.QuantityMax ?? 0;
        if (qtyMax >= UnlimitedQtyMax || qtyMax < 0) qtyMax = 0;
        var minSum = offer.MinSumOrder ?? 0m;
        var typePrice = offer.TypePrice ?? string.Empty;
        // W в type_price — прайс для юр. лиц и ИП; descr_type_price дублирует это текстом.
        var legalOnly = typePrice.Contains('W') || !string.IsNullOrWhiteSpace(offer.TypePriceText);

        var seller = string.IsNullOrWhiteSpace(offer.Seller) ? "Продавец ZZap" : offer.Seller.Trim();
        var location = Clean(offer.Location) ?? Clean(offer.Address);
        var quantity = offer.Quantity ?? 0;

        var conditions = new[]
        {
            Clean(offer.Apply),
            Clean(offer.Shipment),
            minSum > 0m ? $"Минимальная сумма заказа у продавца: {minSum:0.##} ₽" : null,
            pack > 1 ? $"Кратность упаковки: {pack} шт." : null,
            qtyMax > 0 ? $"Максимум к заказу: {qtyMax} шт." : null,
            legalOnly ? Clean(offer.TypePriceText) ?? "Цена только для юр. лиц и ИП" : null,
            offer.Courier == true ? "Есть курьерская доставка" : null,
            Clean(offer.PriceAge) is { } age ? $"Прайс обновлён: {age}" : null
        }.Where(x => x is not null).ToList();

        return new QuotationOffer
        {
            OfferId = offer.CodeDocB,
            ProductName = Clean(offer.Name) ?? article,
            Brand = offer.Brand,
            Article = offer.PartNumber ?? article,
            // Направление — продавец и его точка выдачи, а не склад.
            Warehouse = location is null ? seller : $"{seller} · {location}",
            // qty_v2 < 0 — продавец подтверждает наличие, не раскрывая количество.
            Stock = quantity < 0 ? null : quantity,
            StockText = quantity < 0 ? Clean(offer.QuantityText) ?? "есть" : quantity.ToString(CultureInfo.InvariantCulture),
            MinOrderQuantity = pack,
            DeliveryDaysMin = days,
            DeliveryDaysMax = days,
            Price = new Money(price, "RUB"),
            PriceRub = new Money(price, "RUB"),
            PriceNote = string.Join(" · ", conditions.Append(Attribution)),
            ProviderData = new Dictionary<string, string?>
            {
                ["codeDocB"] = offer.CodeDocB,
                ["sellerKey"] = offer.SellerKey,
                ["sellerName"] = seller,
                ["sellerLocation"] = location,
                ["sellerRating"] = offer.Rating?.ToString(CultureInfo.InvariantCulture),
                ["sellerReviews"] = Clean(offer.RatingCount),
                ["codeRegion"] = codeRegion.ToString(CultureInfo.InvariantCulture),
                ["minSumOrder"] = minSum.ToString("0.##", CultureInfo.InvariantCulture),
                ["pack"] = pack.ToString(CultureInfo.InvariantCulture),
                ["qtyMax"] = qtyMax > 0 ? qtyMax.ToString(CultureInfo.InvariantCulture) : null,
                ["legalOnly"] = legalOnly ? "true" : "false",
                ["typePrice"] = typePrice,
                ["wholesale"] = offer.Wholesale == true ? "true" : "false",
                ["priceDate"] = offer.PriceDate,
                ["attribution"] = Attribution
            }
        };
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
