namespace SupplierQuotationApi.Providers.Japarts;

/// <summary>Настройки Japarts: SUPPLIERS__JAPARTS__*.</summary>
public sealed class JapartsOptions
{
    public const string Section = "Suppliers:Japarts";

    public string? BaseUrl { get; set; } = "https://www.japarts.ru/";
    public string? Login { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password);
}
