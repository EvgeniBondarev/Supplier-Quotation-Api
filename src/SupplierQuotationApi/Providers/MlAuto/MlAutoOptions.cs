namespace SupplierQuotationApi.Providers.MlAuto;

/// <summary>Один аккаунт ML-Auto: SUPPLIERS__MLAUTO__* (Беларусь) или SUPPLIERS__MLAUTORU__* (Россия).</summary>
public sealed class MlAutoOptions
{
    public string? BaseUrl { get; set; }
    public string? Login { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password);
}

/// <summary>Описание контура: ключ, название, страна для подписи склада, валюта цен и секция конфигурации.</summary>
public sealed record MlAutoAccount(string Key, string Name, string Location, string Currency, string ConfigSection,
    string DefaultBaseUrl, string LogoUrl);
