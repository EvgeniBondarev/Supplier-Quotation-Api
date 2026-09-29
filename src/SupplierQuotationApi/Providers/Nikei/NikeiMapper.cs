using System.Globalization;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Nikei;

/// <summary>Преобразует предложения Nikei в единые офферы. Чистая функция, без сети.</summary>
public static class NikeiMapper
{
    public static List<QuotationOffer> MapOriginals(IEnumerable<NikeiPart> parts, string requestedArticle, string? brand,
        ProducerAliasMap? aliases = null)
    {
        aliases ??= ProducerAliasMap.Empty;
        var wanted = Normalize(requestedArticle);
        var result = new List<QuotationOffer>();
        // API иногда повторяет предложение разными to_cart-токенами; product_key — идентификатор одинаковых позиций.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in parts)
        {
            if (part.Price is not { } price || price <= 0m) continue;
            if (Normalize(part.Article) != wanted) continue;
            if (!aliases.Matches(part.Brand, brand)) continue;

            var key = part.ProductKey ?? string.Join('|', part.Article, part.Brand, part.Warehouse, price.ToString(CultureInfo.InvariantCulture));
            if (!seen.Add(key)) continue;

            var min = Math.Max(0, part.DeliveryTime?.Min ?? 0);
            var max = Math.Max(min, part.DeliveryTime?.Max ?? min);
            var stock = part.Stock is >= 0 ? part.Stock : null;

            result.Add(new QuotationOffer
            {
                OfferId = key,
                ProductName = string.IsNullOrWhiteSpace(part.Name) ? part.Article : part.Name,
                Brand = part.Brand,
                Article = part.Article,
                Warehouse = string.IsNullOrWhiteSpace(part.Warehouse) ? null : part.Warehouse,
                Stock = stock,
                StockText = stock?.ToString(CultureInfo.InvariantCulture),
                MinOrderQuantity = Math.Max(1, part.Multiplicity ?? 1),
                DeliveryDaysMin = min,
                DeliveryDaysMax = max,
                Price = new Money(price, "RUB"),
                PriceRub = new Money(price, "RUB"),
                PriceNote = Note(part),
                ProviderData = new Dictionary<string, string?>
                {
                    ["productKey"] = part.ProductKey,
                    ["toCart"] = part.ToCart,
                    ["percent"] = part.Percent?.ToString(CultureInfo.InvariantCulture),
                    ["returnable"] = part.Returnable?.ToString().ToLowerInvariant(),
                    ["returnCostPercent"] = part.ReturnCost?.ToString(CultureInfo.InvariantCulture),
                    ["unsafe"] = part.Unsafe?.ToString().ToLowerInvariant()
                }
            });
        }
        return result;
    }

    private static string? Note(NikeiPart part)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(part.Comment)) parts.Add(part.Comment.Trim());
        if (part.Percent is { } percent) parts.Add($"Процент выдачи: {percent}%");
        if (part.Returnable == true)
            parts.Add(part.ReturnCost is > 0 ? $"Возврат: удержание {part.ReturnCost}%" : "Возврат разрешён");
        else if (part.Returnable == false)
            parts.Add("Возврат не предусмотрен");
        if (part.Unsafe == true) parts.Add("Возможна неточная замена");
        return parts.Count == 0 ? null : string.Join(". ", parts);
    }

    /// <summary>Артикул у Nikei пишется с пробелами («K 1223A»): сравниваем без разделителей и регистра.</summary>
    public static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
