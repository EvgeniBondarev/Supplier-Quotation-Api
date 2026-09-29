namespace SupplierQuotationApi.Providers.ProfitLiga;

/// <summary>Настройки Профит-Лиги: SUPPLIERS__PROFITLIGA__*.</summary>
public sealed class ProfitLigaOptions
{
    public const string Section = "Suppliers:ProfitLiga";

    public string? BaseUrl { get; set; } = "https://api.pr-lg.ru";
    /// <summary>Ключ из личного кабинета pr-lg.ru; API принимает его GET-параметром secret.</summary>
    public string? Secret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Secret);
}
