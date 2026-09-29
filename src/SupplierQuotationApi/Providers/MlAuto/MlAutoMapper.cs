using System.Globalization;
using System.Text.RegularExpressions;
using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.MlAuto;

/// <summary>Преобразует предложения ML-Auto в единые офферы. Чистая функция, без сети.</summary>
public static partial class MlAutoMapper
{
    /// <summary>Даты ML-Auto — местное время UTC+3 (Минск / Москва).</summary>
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(3);

    public static QuotationOffer? Map(MlAutoOffer offer, string location, string currency,
        IReadOnlyDictionary<string, string> conditions, DateTimeOffset now, decimal? rubAmount)
    {
        if (ParseDecimal(offer.Price) is not { } price) return null;

        var storage = offer.StorageCode?.Trim() ?? string.Empty;
        var (stock, stockText) = Stock(offer.Quantity);
        var days = DeliveryDays(offer.Date, now);
        conditions.TryGetValue(storage, out var condition);

        return new QuotationOffer
        {
            OfferId = string.Join("|", offer.Pin, storage, price.ToString(CultureInfo.InvariantCulture), offer.Date),
            ProductName = string.IsNullOrWhiteSpace(offer.Name) ? offer.Pin : offer.Name,
            Brand = offer.Brand,
            Article = offer.Pin,
            // API не отдаёт локацию склада: подпись страны + код склада, иначе склады неразличимы в колонке.
            Warehouse = storage.Length == 0 ? location : $"{location} {storage}",
            Stock = stock,
            StockText = stockText,
            MinOrderQuantity = int.TryParse(offer.Min, out var min) ? Math.Max(1, min) : 1,
            DeliveryDaysMin = days,
            DeliveryDaysMax = days,
            Price = new Money(price, currency),
            PriceRub = rubAmount is null ? null : new Money(rubAmount.Value, "RUB"),
            PriceNote = Note(offer.ReturnPeriod, condition),
            ProviderData = new Dictionary<string, string?>
            {
                ["storageCode"] = offer.StorageCode,
                ["deliveryAt"] = offer.Date,
                ["supplyChance"] = offer.Chance,
                ["returnPeriodDays"] = offer.ReturnPeriod,
                ["deliveryConditions"] = condition
            }
        };
    }

    /// <summary>Срок в днях по календарной дате доставки (UTC+3).</summary>
    public static int? DeliveryDays(string? raw, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            !DateTime.TryParseExact(raw.Trim(), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;

        var today = DateOnly.FromDateTime(now.ToOffset(LocalOffset).DateTime);
        return Math.Max(0, DateOnly.FromDateTime(date).DayNumber - today.DayNumber);
    }

    /// <summary>Наличие бывает «2», «&gt;10», «20&gt;», « 219,00»: знаки сравнения означают «не менее» и снимаются.</summary>
    public static (int? Stock, string? Text) Stock(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var cleaned = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray()).Trim('>', '<', '=', '~', '+');
        if (ParseDecimal(cleaned) is not { } value) return (null, raw.Trim());

        var stock = decimal.ToInt32(Math.Max(0, Math.Floor(value)));
        var text = raw.Contains('>') ? $">{stock}" : raw.Contains('<') ? $"<{stock}" : stock.ToString();
        return (stock, text);
    }

    public static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value?.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : null;

    /// <summary>Убирает HTML-теги из условий поставки.</summary>
    public static string? CleanText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : HtmlTags().Replace(value, string.Empty).Trim();

    private static string? Note(string? returnPeriod, string? condition)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(returnPeriod)) parts.Add($"Возврат: {returnPeriod.Trim()} дн.");
        if (!string.IsNullOrWhiteSpace(condition)) parts.Add(condition);
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex HtmlTags();
}
