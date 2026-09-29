namespace SupplierQuotationApi.Contracts;

/// <summary>Единое ценовое предложение поставщика. Структура одна для всех поставщиков независимо от их API.</summary>
public sealed class QuotationOffer
{
    /// <summary>Стабильный идентификатор предложения у поставщика: нужен для заказа и корзины. Формат зависит от поставщика.</summary>
    public string? OfferId { get; init; }

    /// <summary>Наименование товара так, как его назвал поставщик.</summary>
    public string? ProductName { get; init; }

    /// <summary>Производитель в написании поставщика (может отличаться от запрошенного: <c>FEBI BILSTEIN</c> при запросе <c>Febi</c>).</summary>
    public string? Brand { get; init; }

    /// <summary>Артикул в написании поставщика (<c>K 1223A</c> при запросе <c>K1223A</c>).</summary>
    public string? Article { get; init; }

    /// <summary>Описание товара, если поставщик его отдаёт.</summary>
    public string? Description { get; init; }

    /// <summary>Ссылка на страницу товара; у большинства поставщиков её нет.</summary>
    public string? ProductUrl { get; init; }

    /// <summary>
    /// Склад или направление поставки в читаемом виде. У агрегаторов (АВД, ZZap) это поставщик-продавец и точка выдачи:
    /// <c>LAA (Москва)</c>, <c>ИП Левков · Королев</c>.
    /// </summary>
    public string? Warehouse { get; init; }

    /// <summary>Остаток числом. <c>null</c> — количество скрыто или не удалось разобрать; исходный текст в <see cref="StockText"/>.</summary>
    public int? Stock { get; init; }

    /// <summary>Остаток в исходном виде поставщика: <c>&gt;10</c> («не менее»), <c>под заказ</c>, <c>Заказ</c>.</summary>
    public string? StockText { get; init; }

    /// <summary>Кратность заказа: количество должно делиться на это число. По умолчанию 1.</summary>
    public int MinOrderQuantity { get; init; } = 1;

    /// <summary>Срок доставки, дней (нижняя граница). <c>null</c> — не указан. <c>0</c> — товар на складе.</summary>
    public int? DeliveryDaysMin { get; init; }

    /// <summary>Срок доставки, дней (верхняя граница). Не меньше <see cref="DeliveryDaysMin"/>.</summary>
    public int? DeliveryDaysMax { get; init; }

    /// <summary>Цена закупки за единицу в валюте поставщика (<c>currency</c> внутри).</summary>
    public required Money Price { get; init; }

    /// <summary>Цена за единицу в рублях по курсу ЦБ РФ. Для рублёвых поставщиков совпадает с <see cref="Price"/>. <c>null</c> — курс неизвестен.</summary>
    public Money? PriceRub { get; init; }

    /// <summary>
    /// Условия предложения: возврат, минимальная сумма заказа, ограничения продавца, подпись источника.
    /// У ZZap содержит обязательную подпись «Информация о запчастях предоставлена системой ZZap» — её нужно показывать пользователю.
    /// </summary>
    public string? PriceNote { get; init; }

    /// <summary>
    /// Служебные данные точного предложения для последующего заказа: склад, коды, токены корзины, признаки (<c>isAnalog</c>).
    /// Набор ключей зависит от поставщика; значения — строки. Секретов внутри нет.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? ProviderData { get; init; }
}

/// <summary>Денежная сумма.</summary>
/// <param name="Amount">Сумма за единицу товара.</param>
/// <param name="Currency">Код валюты: RUB, BYN.</param>
public sealed record Money(decimal Amount, string Currency);
