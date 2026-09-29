using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.ForumAuto;

/// <summary>Преобразует строки listgoods в единые офферы. Чистая функция, без сети.</summary>
public static class ForumAutoMapper
{
    public static List<QuotationOffer> Map(IEnumerable<ForumAutoRow> rows, string requestedArticle, string? brand,
        bool includeAnalogs, ProducerAliasMap? aliases = null)
    {
        aliases ??= ProducerAliasMap.Empty;
        var offers = new List<QuotationOffer>();
        var requested = Normalize(requestedArticle);

        foreach (var row in rows)
        {
            if (row.Price is not { } price) continue;
            if (string.IsNullOrWhiteSpace(row.Gid)) continue;

            var isAnalog = Normalize(row.Art) != requested;
            // Контракт: только оригиналы запрошенного артикула, даже если сервер вернул что-то ещё.
            if (isAnalog && !includeAnalogs) continue;
            // Бренд сверяем локально: написания у Forum-Auto свои («FEBI», а не «Febi Bilstein»).
            if (!isAnalog && !aliases.Matches(row.Brand, brand)) continue;

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

    public static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
