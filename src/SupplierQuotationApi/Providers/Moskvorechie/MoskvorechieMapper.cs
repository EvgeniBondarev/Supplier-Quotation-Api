using System.Globalization;
using System.Text.RegularExpressions;
using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.Moskvorechie;

/// <summary>Преобразует данные портала и API в единые офферы. Чистые функции, без сети.</summary>
public static partial class MoskvorechieMapper
{
    public static QuotationOffer MapPortal(MoskvorechiePortalOffer offer)
    {
        var (stock, stockText) = Stock(offer.Rest);
        var days = ParseDays(offer.Delivery);
        return new QuotationOffer
        {
            OfferId = offer.OfferId,
            ProductName = offer.Name,
            Brand = offer.Brand,
            Article = offer.Article,
            Warehouse = offer.Warehouse,
            Stock = stock,
            StockText = stockText,
            MinOrderQuantity = Math.Max(1, offer.MinQuantity),
            DeliveryDaysMin = days,
            DeliveryDaysMax = days,
            Price = new Money(offer.Price, "RUB"),
            PriceRub = new Money(offer.Price, "RUB"),
            PriceNote = string.IsNullOrWhiteSpace(offer.Delivery) ? null : $"Срок: {offer.Delivery}",
            ProviderData = new Dictionary<string, string?> { ["gid"] = offer.OfferId, ["toBasketGid"] = offer.OfferId, ["source"] = "portal" }
        };
    }

    /// <summary>Строка API: артикул должен совпасть, бренд — если указан (API его игнорирует).</summary>
    public static QuotationOffer? MapApi(MoskvorechieApiRow row, string requestedArticle, string? requestedBrand)
    {
        if (row.Price is not { } price || string.IsNullOrWhiteSpace(row.Article)) return null;
        if (MoskvorechiePortalClient.Normalize(row.Article) != MoskvorechiePortalClient.Normalize(requestedArticle)) return null;

        var brand = MoskvorechiePortalClient.Normalize(requestedBrand);
        var actual = MoskvorechiePortalClient.Normalize(row.Brand);
        if (brand.Length > 0 && actual != brand && !actual.Contains(brand) && !brand.Contains(actual)) return null;

        var delivery = string.IsNullOrWhiteSpace(row.Delivery) ? "не известно" : row.Delivery;
        var days = ParseDays(delivery);
        var id = row.Gid ?? $"{row.Article}:{row.Brand}:{price.ToString(CultureInfo.InvariantCulture)}:{delivery}";
        return new QuotationOffer
        {
            OfferId = id,
            ProductName = row.Name ?? row.Article,
            Brand = row.Brand,
            Article = row.Article,
            Warehouse = delivery,   // API склада не отдаёт: в этой колонке — срок, как в Studio2
            Stock = row.Stock,
            StockText = Availability(row.Stock, row.OrderStock),
            MinOrderQuantity = Math.Max(1, row.MinQuantity),
            DeliveryDaysMin = days,
            DeliveryDaysMax = days,
            Price = new Money(price, "RUB"),
            PriceRub = new Money(price, "RUB"),
            ProviderData = new Dictionary<string, string?>
            {
                ["gid"] = row.Gid, ["orderStock"] = row.OrderStock.ToString(), ["source"] = "api"
            }
        };
    }

    /// <summary>Наличие: число или «&gt;20» — знаки сравнения означают «не менее».</summary>
    public static (int? Stock, string? Text) Stock(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var match = Digits().Match(raw);
        if (!match.Success) return (null, raw.Trim());

        var value = int.Parse(match.Value, CultureInfo.InvariantCulture);
        return (value, raw.Contains('>') ? $">{value}" : raw.Contains('<') ? $"<{value}" : value.ToString());
    }

    /// <summary>API отдаёт остаток на складе и «под заказ»: без остатка показываем ступень заказа.</summary>
    public static string Availability(int stock, int orderStock) => stock > 0 ? stock.ToString() : orderStock switch
    {
        >= 500 => ">500", >= 100 => ">100", >= 20 => ">20", > 0 => $">{orderStock}", _ => "0"
    };

    /// <summary>Срок в днях: «3 дня» → 3, «1 день» → 1, «на складе» → 0 (товар в наличии). null — не разобран.</summary>
    public static int? ParseDays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Digits().Match(value);
        if (match.Success && int.TryParse(match.Value, out var days)) return days;
        return value.Contains("склад", StringComparison.OrdinalIgnoreCase) || value.Contains("налич", StringComparison.OrdinalIgnoreCase)
            ? 0
            : null;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
