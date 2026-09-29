using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.ShateM;

/// <summary>Преобразует ответ Шате-М в единый оффер. Чистая функция, без сети.</summary>
public static class ShateMMapper
{
    public const string DefaultCurrency = "BYN";

    public static QuotationOffer? Map(ShateMArticle article, ShateMPrice price,
        IReadOnlyDictionary<string, ShateMLocation> locations, DateTimeOffset now, decimal? rubAmount)
    {
        if (price.Price?.Value is not { } amount) return null;

        var currency = string.IsNullOrWhiteSpace(price.Price.CurrencyCode) ? DefaultCurrency : price.Price.CurrencyCode!.ToUpperInvariant();
        var (daysMin, daysMax) = DeliveryDays(price, now);
        var (stock, stockText) = Stock(price.Quantity);
        var warehouse = FormatWarehouse(price, locations);

        return new QuotationOffer
        {
            OfferId = price.Id,
            ProductName = article.Name,
            Brand = article.TradeMarkName,
            Article = article.Code,
            Description = article.Description is { Length: > 0 } ? article.Description : null,
            Warehouse = warehouse,
            Stock = stock,
            StockText = stockText,
            MinOrderQuantity = price.Quantity?.Minimum is > 0 ? decimal.ToInt32(price.Quantity.Minimum.Value) : 1,
            DeliveryDaysMin = daysMin,
            DeliveryDaysMax = daysMax,
            Price = new Money(amount, currency),
            PriceRub = rubAmount is null ? null : new Money(rubAmount.Value, "RUB"),
            PriceNote = Note(price),
            ProviderData = new Dictionary<string, string?>
            {
                ["articleId"] = article.Id.ToString(),
                ["locationCode"] = price.LocationCode,
                ["locationCodeReal"] = price.LocationCodeReal,
                ["agreementCode"] = price.AgreementCode,
                ["priceHash"] = price.Hash?.ToString(),
                ["multiplicity"] = price.Quantity?.Multiplicity?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                ["supplyRating"] = price.SupplyProbability?.Rating?.ToString()
            }
        };
    }

    /// <summary>Срок по датам доставки; если их нет — по дате отгрузки.</summary>
    public static (int? Min, int? Max) DeliveryDays(ShateMPrice price, DateTimeOffset now)
    {
        var dates = price.DeliveryDateTimes?.Select(x => x.DeliveryDateTime).OfType<DateTimeOffset>().ToList() ?? [];
        if (dates.Count == 0 && price.ShippingDateTime is { } shipping) dates.Add(shipping);
        if (dates.Count == 0) return (null, null);

        var days = dates.Select(d => Math.Max(0, (int)Math.Ceiling((d - now).TotalDays))).ToList();
        return (days.Min(), days.Max());
    }

    /// <summary>Наличие: API отдаёт число и тип («MoreThan» = «не менее»).</summary>
    public static (int? Stock, string? Text) Stock(ShateMQuantity? quantity)
    {
        if (quantity?.Available is not { } available) return (null, null);

        var stock = decimal.ToInt32(Math.Max(0, Math.Floor(available)));
        var text = quantity.AvailableType switch
        {
            "MoreThan" => $">{stock}",
            "LessThan" => $"<{stock}",
            _ => stock.ToString()
        };
        return (stock, text);
    }

    private static string? FormatWarehouse(ShateMPrice price, IReadOnlyDictionary<string, ShateMLocation> locations)
    {
        ShateMLocation? location = null;
        if (price.LocationCode is { Length: > 0 } code) locations.TryGetValue(code, out location);
        if (location is null && price.LocationCodeReal is { Length: > 0 } real) locations.TryGetValue(real, out location);

        if (location is null) return price.LocationCode;
        if (!string.IsNullOrWhiteSpace(location.City) && !string.IsNullOrWhiteSpace(location.Name))
            return $"{location.City} ({location.Name})";
        return location.Name ?? location.City ?? price.LocationCode;
    }

    private static string? Note(ShateMPrice price)
    {
        var text = price.AddInfo?.WarningText ?? price.AddInfo?.Comment;
        return string.IsNullOrWhiteSpace(text) ? null : text.Replace("<br>", " ", StringComparison.OrdinalIgnoreCase).Trim();
    }
}
