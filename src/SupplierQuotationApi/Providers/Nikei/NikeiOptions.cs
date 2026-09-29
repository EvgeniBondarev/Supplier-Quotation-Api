namespace SupplierQuotationApi.Providers.Nikei;

/// <summary>Настройки Nikei: SUPPLIERS__NIKEI__*. Авторизация — HTTP Basic.</summary>
public sealed class NikeiOptions
{
    public const string Section = "Suppliers:Nikei";

    public string? BaseUrl { get; set; } = "https://nikei.ru";
    public string? Login { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password);
}
