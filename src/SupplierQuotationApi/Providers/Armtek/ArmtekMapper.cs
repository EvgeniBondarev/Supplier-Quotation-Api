using System.Globalization;
using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.Armtek;

/// <summary>Преобразует предложение Armtek в единый оффер. Чистая функция, без сети.</summary>
public static class ArmtekMapper
{
    /// <summary>Armtek отдаёт время поставки по Москве (UTC+3, без перехода на летнее).</summary>
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    public static QuotationOffer? Map(ArmtekSearchItem item, IReadOnlyDictionary<string, string> storeNames,
        string vkorg, string? kunnrRg, string? deliveryKunnr, DateTimeOffset now, decimal? rubAmount)
    {
        if (ParseDecimal(item.Price) is not { } price) return null;

        var currency = string.IsNullOrWhiteSpace(item.Currency) ? "RUB" : item.Currency!.ToUpperInvariant();
        var days = DeliveryDays(item.DeliveryDate, now);
        var (stock, stockText) = Stock(item.Rvalue);
        var keyzak = item.Keyzak ?? string.Empty;
        storeNames.TryGetValue(keyzak, out var storeName);

        return new QuotationOffer
        {
            // ARTID — id артикула, одинаковый у всех складов: уникальность даёт связка со складом.
            OfferId = $"{item.ArtId ?? $"{item.Pin}|{item.Brand}"}|{keyzak}|{item.PartnerWarehouseCode}",
            ProductName = string.IsNullOrWhiteSpace(item.Name) ? item.Pin : item.Name,
            Brand = item.Brand,
            Article = item.Pin,
            Warehouse = Warehouse(keyzak, storeName, item.PartnerWarehouseCode),
            Stock = stock,
            StockText = stockText,
            MinOrderQuantity = ParseDecimal(item.MinQuantity) is > 0 and var min ? decimal.ToInt32(min) : 1,
            DeliveryDaysMin = days,
            DeliveryDaysMax = days,
            Price = new Money(price, currency),
            PriceRub = rubAmount is null ? null : new Money(rubAmount.Value, "RUB"),
            PriceNote = string.IsNullOrWhiteSpace(item.Note) ? null : item.Note.Trim(),
            ProviderData = new Dictionary<string, string?>
            {
                ["artId"] = item.ArtId,
                ["keyzak"] = item.Keyzak,
                ["warehouseName"] = storeName,
                ["partnerWarehouseCode"] = item.PartnerWarehouseCode,
                ["deliveryAt"] = item.DeliveryDate,
                ["guaranteedDeliveryAt"] = item.GuaranteedDeliveryDate,
                ["multiplicity"] = item.Multiplicity,
                ["probability"] = item.Probability,
                ["returnDays"] = item.RetDays,
                ["isAnalog"] = item.Analog == "X" ? "true" : "false",
                ["importType"] = item.TypeB,
                ["decree713"] = item.Dspec,
                ["maxRetailPrice"] = item.MaxRetailPrice,
                ["vkorg"] = vkorg,
                ["kunnrRg"] = kunnrRg,
                ["deliveryKunnr"] = deliveryKunnr
            }
        };
    }

    /// <summary>DLVDT в формате yyyyMMddHHmmss (Москва). null — дата не разобрана.</summary>
    public static int? DeliveryDays(string? raw, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            !DateTime.TryParseExact(raw.Trim(), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;

        var delivery = new DateTimeOffset(local, MoscowOffset);
        return Math.Max(0, (int)Math.Ceiling((delivery - now).TotalDays));
    }

    /// <summary>RVALUE — число, «>100» или словами («меньше 100»): число извлекается, текст приводится к «&lt;100» / «&gt;100».</summary>
    public static (int? Stock, string? Text) Stock(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        var text = raw.Trim();
        var digits = System.Text.RegularExpressions.Regex.Match(text, @"\d+([.,]\d+)?");
        if (!digits.Success) return (null, text);

        var stock = decimal.ToInt32(Math.Floor(ParseDecimal(digits.Value) ?? 0));
        var lower = text.ToLowerInvariant();
        var normalized = lower.Contains("меньше") || lower.StartsWith('<') ? $"<{stock}"
            : lower.Contains("больше") || lower.StartsWith('>') ? $">{stock}"
            : text;
        return (stock, normalized);
    }

    private static string? Warehouse(string keyzak, string? storeName, string? partnerCode)
    {
        if (!string.IsNullOrWhiteSpace(storeName)) return $"{storeName} ({keyzak})";
        if (!string.IsNullOrWhiteSpace(keyzak)) return keyzak;
        return string.IsNullOrWhiteSpace(partnerCode) ? null : $"Партнёрский склад ({partnerCode})";
    }

    public static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value?.Replace(" ", string.Empty).Replace(',', '.'), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
}
