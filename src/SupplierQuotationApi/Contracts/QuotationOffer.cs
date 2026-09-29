namespace SupplierQuotationApi.Contracts;

/// <summary>Единое ценовое предложение поставщика.</summary>
public sealed class QuotationOffer
{
    /// <summary>Стабильный идентификатор предложения у поставщика (для заказа/корзины).</summary>
    public string? OfferId { get; init; }
    public string? ProductName { get; init; }
    public string? Brand { get; init; }
    public string? Article { get; init; }
    public string? Description { get; init; }
    public string? ProductUrl { get; init; }

    /// <summary>Склад / направление поставки в читаемом виде.</summary>
    public string? Warehouse { get; init; }

    /// <summary>Остаток. null — не удалось разобрать число, см. <see cref="StockText"/>.</summary>
    public int? Stock { get; init; }
    /// <summary>Остаток в исходном виде поставщика («>10», «под заказ»).</summary>
    public string? StockText { get; init; }
    /// <summary>Минимальная кратность заказа.</summary>
    public int MinOrderQuantity { get; init; } = 1;

    public int? DeliveryDaysMin { get; init; }
    public int? DeliveryDaysMax { get; init; }

    /// <summary>Цена закупки в валюте поставщика.</summary>
    public required Money Price { get; init; }
    /// <summary>Цена, пересчитанная в RUB. null — не пересчитывалась.</summary>
    public Money? PriceRub { get; init; }
    public string? PriceNote { get; init; }

    /// <summary>Служебные данные точного предложения для последующего заказа (без секретов).</summary>
    public IReadOnlyDictionary<string, string?>? ProviderData { get; init; }
}

public sealed record Money(decimal Amount, string Currency);
