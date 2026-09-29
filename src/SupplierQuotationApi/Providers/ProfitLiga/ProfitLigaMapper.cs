using System.Globalization;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.ProfitLiga;

/// <summary>Преобразует карточки Профит-Лиги в единые офферы. Чистая функция, без сети.</summary>
public static class ProfitLigaMapper
{
    /// <summary>Предложения только по запрошенному артикулу и бренду: карточка ответа может относиться к другому товару,
    /// а замены в проценку не входят.</summary>
    public static List<QuotationOffer> MapOriginals(IEnumerable<ProfitLigaCard> cards, string article, string? brand,
        ProducerAliasMap? aliases = null)
    {
        aliases ??= ProducerAliasMap.Empty;
        var wanted = Normalize(article);
        var offers = new List<QuotationOffer>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var card in cards)
        {
            if (Normalize(card.Article) != wanted) continue;
            if (!aliases.Matches(card.Brand, brand)) continue;

            foreach (var offer in card.Offers)
                if (Map(offer, card) is { } mapped && seen.Add(mapped.OfferId!))
                    offers.Add(mapped);
        }
        return offers;
    }

    private static QuotationOffer? Map(ProfitLigaOffer offer, ProfitLigaCard card)
    {
        if (offer.Price is not { } price || price <= 0m) return null;

        var quantity = offer.Quantity ?? 0m;
        var days = DeliveryDays(offer.DeliveryHours);
        var article = offer.Article ?? card.Article;
        var warehouse = string.IsNullOrWhiteSpace(offer.WarehouseName) ? offer.WarehouseId : offer.WarehouseName;

        return new QuotationOffer
        {
            // Хеш предложения устойчив на стороне поставщика; без него — пара «артикул|склад», как ключ корзины.
            OfferId = string.IsNullOrWhiteSpace(offer.Key) ? $"{offer.ArticleId}|{offer.WarehouseId}" : offer.Key,
            // В прайсовой строке наименование искажено (1/4" приходит как «1 4&amp;quot,.»): основным берём описание карточки.
            ProductName = DecodeHtml(card.Description) ?? DecodeHtml(offer.Description) ?? article,
            Brand = offer.Brand ?? card.Brand,
            Article = article,
            Warehouse = warehouse,
            Stock = decimal.ToInt32(Math.Max(0, Math.Floor(quantity))),
            StockText = quantity.ToString("0.##", CultureInfo.InvariantCulture),
            MinOrderQuantity = offer.Multi is > 0 ? offer.Multi.Value : 1,
            DeliveryDaysMin = days,
            DeliveryDaysMax = days,
            Price = new Money(price, "RUB"),
            PriceRub = new Money(price, "RUB"),
            PriceNote = Note(offer),
            ProviderData = new Dictionary<string, string?>
            {
                ["articleId"] = offer.ArticleId,
                ["warehouseId"] = offer.WarehouseId,
                ["productCode"] = offer.ProductCode,
                ["multi"] = offer.Multi is > 0 ? offer.Multi.Value.ToString(CultureInfo.InvariantCulture) : null,
                ["deliveryHours"] = offer.DeliveryHours?.ToString(CultureInfo.InvariantCulture),
                ["deliveryAt"] = offer.DeliveryDate,
                ["deliveryProbability"] = offer.Probability?.ToString("0.#", CultureInfo.InvariantCulture),
                ["returnAllowed"] = offer.AllowReturn ? "true" : "false",
                ["waitings"] = offer.Waitings is > 0 ? offer.Waitings.Value.ToString("0.##", CultureInfo.InvariantCulture) : null,
                ["sale"] = offer.Sale ? "true" : null
            }
        };
    }

    /// <summary>API отдаёт срок в часах, единая структура — в днях: округление вверх.</summary>
    public static int DeliveryDays(int? hours) => hours is null or <= 0 ? 0 : (int)Math.Ceiling(hours.Value / 24m);

    private static string? Note(ProfitLigaOffer offer)
    {
        var parts = new List<string>();
        if (offer.AllowReturn)
            parts.Add(offer.ReturnDays is > 0 ? $"Возврат: до {offer.ReturnDays} дн." : "Возврат разрешён");
        else
            parts.Add("Возврат не разрешён");
        if (offer.Sale) parts.Add(string.IsNullOrWhiteSpace(offer.Comment) ? "Уценённый товар" : $"Уценка: {offer.Comment}");
        return string.Join(". ", parts);
    }

    /// <summary>Наименования приходят с HTML-сущностями, в строках прайса — с двойным экранированием: двух проходов достаточно.</summary>
    public static string? DecodeHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var decoded = System.Net.WebUtility.HtmlDecode(value);
        if (decoded.Contains('&') && decoded.Contains(';')) decoded = System.Net.WebUtility.HtmlDecode(decoded);
        return decoded.Trim();
    }

    /// <summary>Артикулы у поставщика и в заказе различаются разделителями и регистром.</summary>
    public static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
