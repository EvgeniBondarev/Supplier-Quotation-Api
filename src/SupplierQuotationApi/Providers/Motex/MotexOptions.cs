namespace SupplierQuotationApi.Providers.Motex;

/// <summary>Настройки МоТехС: SUPPLIERS__MOTEX__*. API доступен только с IP из белого списка клиента.</summary>
public sealed class MotexOptions
{
    public const string Section = "Suppliers:Motex";

    public string? BaseUrl { get; set; } = "https://api.motexc.by";
    public string? Login { get; set; }
    public string? Password { get; set; }
    /// <summary>Валюта цен. В ответе она не приходит поэлементно: белорусский поставщик, по умолчанию BYN.</summary>
    public string? Currency { get; set; } = "BYN";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password);
}
