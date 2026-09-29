namespace SupplierQuotationApi.Providers.Mikado;

/// <summary>Микадо (склад МскХод20): SUPPLIERS__MIKADOMSKHOD20__*. Доступ ограничен по IP клиента на стороне Микадо.</summary>
public sealed class MikadoOptions
{
    public const string Section = "Suppliers:MikadoMskHod20";

    public string? BaseUrl { get; set; } = "https://www.mikado-parts.ru/ws1";
    public string? ClientId { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Password);
}
