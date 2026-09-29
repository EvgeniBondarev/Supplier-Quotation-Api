using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.ForumAuto;

/// <summary>Преобразует строки listgoods в единые офферы. Чистая функция, без сети.</summary>
public static class ForumAutoMapper
{
    public static List<QuotationOffer> Map(IEnumerable<ForumAutoRow> rows, string requestedArticle, string? brand,
        bool includeAnalogs)
    {
        var offers = new List<QuotationOffer>();
        var requested = Normalize(requestedArticle);
        var requestedBrand = Normalize(brand);

        foreach (var row in rows)
        {
            if (row.Price is not { } price) continue;
            if (string.IsNullOrWhiteSpace(row.Gid)) continue;

            var isAnalog = Normalize(row.Art) != requested;
            // Контракт: только оригиналы запрошенного артикула, даже если сервер вернул что-то ещё.
            if (isAnalog && !includeAnalogs) continue;
            // Бренд сверяем локально: написания у Forum-Auto свои («FEBI», а не «Febi Bilstein»).
            if (!isAnalog && !BrandMatches(row.Brand, requestedBrand)) continue;

            var days = row.DeliveryDays;
            // h_deliv — полный срок в часах: верхняя граница не меньше дней, округляем часы вверх.
            var maxDays = row.DeliveryHours is > 0 ? Math.Max(days ?? 0, (int)Math.Ceiling(row.DeliveryHours.Value / 24.0)) : days;
            var returnable = row.IsReturnable == 1;

            offers.Add(new QuotationOffer
            {
                OfferId = row.Gid,
                ProductName = row.Name ?? row.Art,
                Brand = row.Brand,
                Article = row.Art,
                Warehouse = string.IsNullOrWhiteSpace(row.Warehouse) ? null : row.Warehouse.Trim(),
                Stock = row.Stock,
                StockText = row.Stock?.ToString(),
                MinOrderQuantity = row.Multiplicity is > 0 ? row.Multiplicity.Value : 1,
                DeliveryDaysMin = days,
                DeliveryDaysMax = maxDays,
                Price = new Money(price, "RUB"),
                PriceRub = new Money(price, "RUB"),
                PriceNote = returnable ? null : "Возврат не разрешён",
                ProviderData = new Dictionary<string, string?>
                {
                    ["gid"] = row.Gid,
                    ["deliveryHours"] = row.DeliveryHours?.ToString(),
                    ["isAnalog"] = isAnalog ? "true" : "false",
                    ["returnable"] = returnable ? "true" : "false"
                }
            });
        }
        return offers;
    }

    private static bool BrandMatches(string? actual, string requestedBrand)
    {
        if (requestedBrand.Length == 0) return true;
        var brand = Normalize(actual);
        return brand == requestedBrand || (brand.Length > 0 && (brand.Contains(requestedBrand) || requestedBrand.Contains(brand)));
    }

    public static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
