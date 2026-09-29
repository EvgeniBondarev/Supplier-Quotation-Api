using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.Avd;

/// <summary>Преобразует предложения АВД в единые офферы. Чистая функция, без сети.</summary>
public static class AvdMapper
{
    public static List<QuotationOffer> Map(IEnumerable<AvdOffer> offers)
    {
        var result = new List<QuotationOffer>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var offer in offers)
        {
            if (offer.Price is not { } price) continue;
            // Хэш кодирует конкретное предложение; одинаковый хэш — дубль строки.
            if (offer.Hash is not null && !seen.Add(offer.Hash)) continue;

            var warehouse = Warehouse(offer);
            var days = offer.SupplierPeriod;
            result.Add(new QuotationOffer
            {
                OfferId = offer.Hash,
                ProductName = offer.ItemName ?? offer.ItemNumber,
                Brand = offer.CatalogName,
                Article = offer.ItemNumber,
                Warehouse = warehouse,
                Stock = offer.Quantity is { } q ? decimal.ToInt32(Math.Max(0, Math.Floor(q))) : null,
                StockText = offer.Quantity?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                MinOrderQuantity = offer.Multiply is > 0 ? offer.Multiply.Value : 1,
                DeliveryDaysMin = days,
                DeliveryDaysMax = days,
                Price = new Money(price, "RUB"),
                PriceRub = new Money(price, "RUB"),
                PriceNote = Note(offer),
                ProviderData = new Dictionary<string, string?>
                {
                    ["hash"] = offer.Hash,
                    ["supplierName"] = offer.SupplierName,
                    ["supplierRegion"] = offer.SupplierRegion,
                    ["dealerStore"] = offer.DealerStore,
                    ["averageDelivery"] = offer.PriceAverage,
                    ["deliveryStatistic"] = offer.PriceStatistic,
                    ["priceDate"] = offer.DatePrice,
                    ["prepay"] = offer.SupplierPrepay,
                    ["returnAllowed"] = offer.SupplierReturn,
                    ["isOriginal"] = offer.IsOriginal?.ToString().ToLowerInvariant()
                }
            });
        }
        return result;
    }

    /// <summary>«Поставщик (регион)»; у АВД склад — это поставщик-агрегатор, отдельного кода склада нет.</summary>
    private static string? Warehouse(AvdOffer offer)
    {
        var name = offer.DealerStore ?? offer.SupplierName;
        if (name is null) return offer.SupplierRegion;
        return offer.SupplierRegion is null ? name : $"{name} ({offer.SupplierRegion})";
    }

    private static string? Note(AvdOffer offer)
    {
        var parts = new[] { offer.SupplierInfo, offer.SupplierReturnDescription }.Where(x => !string.IsNullOrWhiteSpace(x));
        var text = string.Join(" ", parts);
        return text.Length == 0 ? null : text;
    }

    public static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
