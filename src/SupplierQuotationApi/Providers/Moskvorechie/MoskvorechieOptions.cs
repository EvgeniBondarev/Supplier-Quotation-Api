namespace SupplierQuotationApi.Providers.Moskvorechie;

/// <summary>Москворечье (Истра): SUPPLIERS__MOSKVORECHIEISTRA__*.</summary>
public sealed class MoskvorechieOptions
{
    public const string Section = "Suppliers:MoskvorechieIstra";
    public const string DefaultPortalUrl = "https://portal.moskvorechie.ru/";

    /// <summary>Адрес JSON API (portal.api).</summary>
    public string? BaseUrl { get; set; } = "https://portal.moskvorechie.ru/portal.api";
    public string? Login { get; set; }
    public string? ApiKey { get; set; }
    /// <summary>Пароль портала: нужен для точной проценки (склад, наличие, срок). Без него работает только API без бренда.</summary>
    public string? PortalPassword { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Login) &&
                                (!string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(PortalPassword));
    public bool PortalConfigured => !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(PortalPassword);
    public bool ApiConfigured => !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(BaseUrl);
}

public sealed class MoskvorechieApiException(string message) : Exception(message);

/// <summary>Строка с портала (склад, наличие, срок, цена).</summary>
public sealed record MoskvorechiePortalOffer(string OfferId, string Brand, string Article, string Name, string Rest,
    string Warehouse, string Delivery, int MinQuantity, decimal Price);
