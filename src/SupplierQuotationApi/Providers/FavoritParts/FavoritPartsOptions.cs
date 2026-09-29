namespace SupplierQuotationApi.Providers.FavoritParts;

/// <summary>Один аккаунт FavoritParts: SUPPLIERS__FAVORITPARTS__*, ...ISTRA__*, ...ROSTOV__*.</summary>
public sealed class FavoritPartsOptions
{
    public string? BaseUrl { get; set; } = "https://api.favorit-parts.ru";
    /// <summary>Ключ покупателя (key).</summary>
    public string? ClientKey { get; set; }
    /// <summary>Ключ разработчика (developerKey).</summary>
    public string? DeveloperKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ClientKey);
}

public sealed record FavoritPartsAccount(string Key, string Name, string LogoFile, string ConfigSection, string HttpClientName);
