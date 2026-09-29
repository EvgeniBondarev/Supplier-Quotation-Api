using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.Berg;

/// <summary>Преобразует ответ Berg (карточка → предложения складов) в единые офферы. Чистая функция, без сети.</summary>
public static class BergMapper
{
    public static List<QuotationOffer> Map(IEnumerable<BergResource> resources, string requestedArticle, string? brand,
        bool includeAnalogs, DateOnly today)
    {
        var offers = new List<QuotationOffer>();
        var requested = Normalize(requestedArticle);
        var requestedBrand = Normalize(brand);

        foreach (var resource in resources)
        {
            var isAnalog = Normalize(resource.Article) != requested;
            // Контракт: только оригиналы запрошенного артикула, даже если сервер вернул что-то ещё.
            if (isAnalog && !includeAnalogs) continue;
            // Берг параметр brand_name фактически игнорирует и отдаёт все бренды артикула — бренд отсекаем здесь.
            if (!isAnalog && !BrandMatches(resource.Brand?.Name, requestedBrand)) continue;

            foreach (var offer in resource.Offers ?? [])
            {
                if (offer.Price is not { } price) continue;

                var (min, max) = DeliveryDays(offer, today);
                var quantity = offer.Quantity;
                offers.Add(new QuotationOffer
                {
                    OfferId = $"{resource.Id}|{offer.Warehouse?.Id}",
                    ProductName = resource.Name ?? resource.Article,
                    Brand = resource.Brand?.Name,
                    Article = resource.Article,
                    Warehouse = offer.Warehouse?.Name,
                    Stock = quantity is { } q ? decimal.ToInt32(Math.Max(0, Math.Floor(q))) : null,
                    StockText = quantity is null ? null : offer.AvailableMore == true
                        ? $">{quantity:0.##}"
                        : quantity.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    MinOrderQuantity = offer.MultiplicationFactor is > 0 ? offer.MultiplicationFactor.Value : 1,
                    DeliveryDaysMin = min,
                    DeliveryDaysMax = max,
                    Price = new Money(price, "RUB"),
                    PriceRub = new Money(price, "RUB"),
                    PriceNote = offer.IsTransit == true ? "Транзит" : null,
                    ProviderData = new Dictionary<string, string?>
                    {
                        ["resourceId"] = resource.Id?.ToString(),
                        ["warehouseId"] = offer.Warehouse?.Id?.ToString(),
                        ["reliability"] = offer.Reliability?.ToString(),
                        ["assuredPeriodDays"] = offer.AssuredPeriod?.ToString(),
                        ["averagePeriodDays"] = offer.AveragePeriod?.ToString(),
                        ["isTransit"] = offer.IsTransit?.ToString().ToLowerInvariant(),
                        ["availableMore"] = offer.AvailableMore?.ToString().ToLowerInvariant(),
                        ["isAnalog"] = isAnalog ? "true" : "false"
                    }
                });
            }
        }
        return offers;
    }

    /// <summary>Если есть расписание доставки на адрес — по датам первого слота; иначе гарантированный и средний срок.</summary>
    public static (int? Min, int? Max) DeliveryDays(BergOffer offer, DateOnly today)
    {
        var slot = offer.AddressTimetable?.FirstOrDefault(x => ParseDate(x.DeliveryFrom).HasValue);
        if (ParseDate(slot?.DeliveryFrom) is { } from)
        {
            var min = Math.Max(0, from.DayNumber - today.DayNumber);
            var max = ParseDate(slot!.DeliveryTo) is { } to ? Math.Max(min, to.DayNumber - today.DayNumber) : min;
            return (min, max);
        }

        if (offer.AssuredPeriod is null && offer.AveragePeriod is null) return (null, null);
        var assured = offer.AssuredPeriod ?? offer.AveragePeriod ?? 0;
        return (assured, Math.Max(assured, offer.AveragePeriod ?? assured));
    }

    /// <summary>Дата слота «yyyy-MM-dd HH:mm:ss» (время Москвы). null — не разобрана.</summary>
    public static DateOnly? ParseDate(string? raw) =>
        DateTime.TryParseExact(raw?.Trim(), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var value)
            ? DateOnly.FromDateTime(value)
            : null;

    /// <summary>Бренд: точно, по вхождению или через транслитерацию (TecDoc «LUKOIL» ↔ Berg «ЛУКОЙЛ»).</summary>
    public static bool BrandMatches(string? actual, string requestedBrand)
    {
        if (requestedBrand.Length == 0) return true;
        foreach (var candidate in new[] { Normalize(actual), Normalize(Transliterate(actual, true)), Normalize(Transliterate(actual, false)) })
            if (candidate.Length > 0 && (candidate == requestedBrand || candidate.Contains(requestedBrand) || requestedBrand.Contains(candidate)))
                return true;
        return false;
    }

    private static readonly Dictionary<char, string> CyrillicToLatin = new()
    {
        ['А'] = "A", ['Б'] = "B", ['В'] = "V", ['Г'] = "G", ['Д'] = "D", ['Е'] = "E", ['Ё'] = "E", ['Ж'] = "ZH", ['З'] = "Z",
        ['И'] = "I", ['К'] = "K", ['Л'] = "L", ['М'] = "M", ['Н'] = "N", ['О'] = "O", ['П'] = "P", ['Р'] = "R", ['С'] = "S",
        ['Т'] = "T", ['У'] = "U", ['Ф'] = "F", ['Х'] = "KH", ['Ц'] = "TS", ['Ч'] = "CH", ['Ш'] = "SH", ['Щ'] = "SHCH",
        ['Ъ'] = "", ['Ы'] = "Y", ['Ь'] = "", ['Э'] = "E", ['Ю'] = "YU", ['Я'] = "YA"
    };

    /// <summary>«Й» транслитерируется по-разному («май» → may, «Лукойл» → Lukoil): пробуем оба варианта.</summary>
    private static string Transliterate(string? value, bool iotaAsY)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.ToUpperInvariant())
        {
            if (ch == 'Й') { builder.Append(iotaAsY ? "Y" : "I"); continue; }
            builder.Append(CyrillicToLatin.TryGetValue(ch, out var mapped) ? mapped : ch.ToString());
        }
        return builder.ToString();
    }

    public static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
