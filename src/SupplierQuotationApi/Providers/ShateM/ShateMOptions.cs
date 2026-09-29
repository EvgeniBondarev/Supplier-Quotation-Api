namespace SupplierQuotationApi.Providers.ShateM;

/// <summary>Настройки Шате-М: SUPPLIERS__SHATEM__*.</summary>
public sealed class ShateMOptions
{
    public const string Section = "Suppliers:ShateM";

    public string? BaseUrl { get; set; }
    public string? Login { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password);
}
