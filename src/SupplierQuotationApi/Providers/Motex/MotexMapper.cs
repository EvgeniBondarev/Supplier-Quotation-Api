using System.Globalization;
using System.Text.RegularExpressions;
using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.Motex;

/// <summary>Преобразует предложения МоТехС в единые офферы. Чистая функция, без сети.</summary>
public static partial class MotexMapper
{
    /// <summary>Даты МоТехС — местное время Минска (UTC+3).</summary>
    private static readonly TimeSpan MinskOffset = TimeSpan.FromHours(3);

    public static QuotationOffer? Map(MotexArticle article, string currency, DateTimeOffset now, decimal? rubAmount)
    {
        if (ParseNumber(article.Price) is not { } price) return null;

        var quantity = ParseNumber(article.Quantity);
        var multiple = ParseNumber(article.Multiple);
        var storeCode = article.StoreCode ?? string.Empty;
        var days = DeliveryDays(article.DeliveryDate, now);

        return new QuotationOffer
        {
            OfferId = $"{article.Code}|{storeCode}|{price.ToString(CultureInfo.InvariantCulture)}",
            ProductName = string.IsNullOrWhiteSpace(article.Description) ? article.Code : article.Description,
            Brand = article.Brand,
            Article = article.Code,
            Warehouse = string.IsNullOrWhiteSpace(article.StoreInfo) ? (storeCode.Length == 0 ? null : storeCode) : article.StoreInfo,
            Stock = quantity is null ? null : decimal.ToInt32(Math.Max(0, Math.Floor(quantity.Value))),
            StockText = StockText(article.Quantity, quantity),
            MinOrderQuantity = multiple is null ? 1 : decimal.ToInt32(Math.Max(1m, multiple.Value)),
            DeliveryDaysMin = days,
            DeliveryDaysMax = days,
            Price = new Money(price, currency),
            PriceRub = rubAmount is null ? null : new Money(rubAmount.Value, "RUB"),
            PriceNote = article.ReturnPeriod is { } returnDays ? $"Возврат: {returnDays} дн." : null,
            ProviderData = new Dictionary<string, string?>
            {
                ["storeCode"] = article.StoreCode,
                ["storeInfo"] = article.StoreInfo,
                ["deliveryAt"] = article.DeliveryDate,
                ["returnPeriodDays"] = article.ReturnPeriod?.ToString(),
                ["decree713"] = article.ResolutionTag == 1 ? "true" : null,
                ["remainingMarkup"] = article.ResolutionTag == 1 ? article.RemainingMarkup : null
            }
        };
    }

    public static int? DeliveryDays(string? raw, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            !DateTime.TryParseExact(raw.Trim(), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;

        return Math.Max(0, (int)Math.Ceiling((new DateTimeOffset(local, MinskOffset) - now).TotalDays));
    }

    /// <summary>Число из строки с суффиксом: «20>» → 20. null — числа нет.</summary>
    public static decimal? ParseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var match = NumberRegex().Match(raw);
        return match.Success && decimal.TryParse(match.Value.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>«20>» (от 20 шт.) → «&gt;20»; обычное число остаётся как есть.</summary>
    private static string? StockText(string? raw, decimal? quantity)
    {
        if (quantity is null) return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        var number = quantity.Value.ToString("0.##", CultureInfo.InvariantCulture);
        return raw is not null && raw.Contains('>') ? $">{number}" : raw is not null && raw.Contains('<') ? $"<{number}" : number;
    }

    [GeneratedRegex(@"[-+]?[0-9]+(?:[.,][0-9]+)?")]
    private static partial Regex NumberRegex();
}
