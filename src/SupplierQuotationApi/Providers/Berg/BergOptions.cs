namespace SupplierQuotationApi.Providers.Berg;

/// <summary>Настройки Berg: SUPPLIERS__BERG__*.</summary>
public sealed class BergOptions
{
    public const string Section = "Suppliers:Berg";

    public string? BaseUrl { get; set; } = "https://api.berg.ru";
    /// <summary>Ключ из личного кабинета berg.ru, уходит заголовком X-Berg-API-Key.</summary>
    public string? ApiKey { get; set; }
    /// <summary>Адрес отгрузки (references/shipment_address). От него зависят сроки доставки.
    /// Пусто — первый активный адрес аккаунта.</summary>
    public string? DeliveryAddressId { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}
