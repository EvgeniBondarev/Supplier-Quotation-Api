using System.Globalization;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Japarts;

/// <summary>Преобразует предложения Japarts в единые офферы. Чистая функция, без сети.</summary>
public static class JapartsMapper
{
    public static List<QuotationOffer> Map(IEnumerable<JapartsOffer> offers, string requestedArticle, string? brand,
        ProducerAliasMap? aliases = null)
    {
        aliases ??= ProducerAliasMap.Empty;
        var wanted = Normalize(requestedArticle);
        var result = new List<QuotationOffer>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var offer in offers)
        {
            if (offer.PriceRub is not { } price || string.IsNullOrEmpty(offer.PriceId)) continue;
            // Контракт: только оригиналы запрошенного номера (cross=0 запрошен, но фильтруем и на своей стороне).
            if (Normalize(offer.DetailNum) != wanted) continue;
            if (!aliases.Matches(offer.MakeName, brand)) continue;
            if (!seen.Add(offer.PriceId)) continue;

            var days = offer.DeliveryDays;
            var warehouse = string.Join(" ", new[] { offer.Country, offer.SupplierCode }.Where(x => !string.IsNullOrWhiteSpace(x)));
            result.Add(new QuotationOffer
            {
                OfferId = offer.PriceId,
                ProductName = offer.DetailName,
                Brand = offer.MakeName,
                Article = offer.DetailNum,
                Warehouse = warehouse.Length == 0 ? null : warehouse,
                Stock = offer.Quantity is { } q ? decimal.ToInt32(Math.Max(0, Math.Floor(q))) : null,
                StockText = offer.Quantity?.ToString("0.##", CultureInfo.InvariantCulture),
                MinOrderQuantity = offer.Lot is >= 1 ? decimal.ToInt32(offer.Lot.Value) : 1,
                DeliveryDaysMin = days,
                // Гарантированный срок — верхняя граница; не меньше обычного.
                DeliveryDaysMax = offer.GuaranteedDeliveryDays is { } guaranteed ? Math.Max(guaranteed, days ?? 0) : days,
                Price = new Money(price, "RUB"),
                PriceRub = new Money(price, "RUB"),
                PriceNote = Note(offer),
                ProviderData = new Dictionary<string, string?>
                {
                    ["priceId"] = offer.PriceId,
                    ["supplierCode"] = offer.SupplierCode,
                    ["lot"] = offer.Lot?.ToString(CultureInfo.InvariantCulture),
                    ["deposit"] = offer.Deposit ? "1" : "0",
                    ["deliveryStatistic"] = offer.Statistic?.ToString(CultureInfo.InvariantCulture),
                    ["unconditionalReturn"] = offer.UnconditionalReturn ? "true" : "false",
                    ["refurbished"] = offer.Refurbished ? "true" : null
                }
            });
        }
        return result;
    }

    private static string? Note(JapartsOffer offer)
    {
        var parts = new List<string>();
        if (offer.UnconditionalReturn) parts.Add("Безусловный возврат");
        if (offer.Deposit) parts.Add("Требуется депозит");
        if (offer.Refurbished) parts.Add("Восстановленный");
        if (offer.Statistic is { } statistic) parts.Add($"Статистика поставщика: {statistic}%");
        return parts.Count == 0 ? null : string.Join(". ", parts);
    }

    public static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
