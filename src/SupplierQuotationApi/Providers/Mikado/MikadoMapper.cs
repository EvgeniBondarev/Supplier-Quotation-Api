using System.Globalization;
using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.Mikado;

/// <summary>Преобразует строки Микадо в единые офферы. Чистая функция, без сети.</summary>
public static class MikadoMapper
{
    public static List<QuotationOffer> Map(IEnumerable<MikadoLine> lines, string requestedArticle)
    {
        var result = new List<QuotationOffer>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            if (!decimal.TryParse(line.PriceRur, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price <= 0) continue;

            var stockId = line.StockId ?? string.Empty;
            // Из OrderCode и StokID корзина потом восстанавливает код заказа и склад.
            var id = string.Join("|", line.OrderCode, stockId, line.PriceRur);
            if (!seen.Add(id)) continue;

            var days = Days(line.DeliveryDelay ?? line.StockDelay);
            var stock = Digits(line.StockQty);
            result.Add(new QuotationOffer
            {
                OfferId = id,
                ProductName = line.Name ?? requestedArticle,
                Brand = line.Brand,
                // В строке нет артикула, только внутренний OrderCode: артикул — тот, что запрошен (поиск по коду и бренду точный).
                Article = requestedArticle,
                Warehouse = line.StockName ?? (stockId.Length == 0 ? null : stockId),
                Stock = stock,
                StockText = line.StockQty,
                MinOrderQuantity = int.TryParse(line.MinOrderQty, out var min) ? Math.Max(1, min) : 1,
                DeliveryDaysMin = days,
                DeliveryDaysMax = days,
                Price = new Money(price, "RUB"),
                PriceRub = new Money(price, "RUB"),
                ProviderData = new Dictionary<string, string?>
                {
                    ["orderCode"] = line.OrderCode,
                    ["stockId"] = line.StockId,
                    ["certificateUrl"] = line.CertificateUrl
                }
            });
        }
        return result;
    }

    /// <summary>Срок — цифры из DeliveryDelay (иначе StockDelay); нет данных — 0, как в Studio2.</summary>
    public static int Days(string? raw) => Digits(raw) ?? 0;

    private static int? Digits(string? raw)
    {
        var digits = new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : null;
    }
}
