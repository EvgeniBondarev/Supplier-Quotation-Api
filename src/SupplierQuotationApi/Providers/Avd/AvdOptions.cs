namespace SupplierQuotationApi.Providers.Avd;

/// <summary>Настройки АВД: SUPPLIERS__AVD__*.</summary>
public sealed class AvdOptions
{
    public const string Section = "Suppliers:Avd";

    public string? BaseUrl { get; set; } = "https://ws1.avdmotors.ru/AvdUserService.svc/secure";
    public string? Login { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password);
}
