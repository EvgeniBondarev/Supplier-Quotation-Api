namespace SupplierQuotationApi.Providers.Armtek;

/// <summary>Один аккаунт Armtek: SUPPLIERS__ARMTEK__* или SUPPLIERS__ARMTEKBY__*.</summary>
public sealed class ArmtekOptions
{
    public string? BaseUrl { get; set; } = "https://ws.armtek.ru";
    public string? User { get; set; }
    public string? Password { get; set; }
    /// <summary>Сбытовая организация: 4000 — Россия, 2000 — Беларусь.</summary>
    public string? DefaultVkorg { get; set; }
    /// <summary>KUNNR_RG покупателя. Пусто — покупатель по умолчанию из getUserInfo.</summary>
    public string? BuyerKunnr { get; set; }
    /// <summary>KUNNR_ZA — адрес доставки. От него зависят доступные склады: без подходящего адреса
    /// аккаунт RU может ничего не находить. Пусто — поиск без адреса.</summary>
    public string? DeliveryKunnr { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(User) &&
        !string.IsNullOrWhiteSpace(Password) && !string.IsNullOrWhiteSpace(DefaultVkorg);
}

/// <summary>Описание аккаунта: ключ, название, логотип и секция конфигурации.</summary>
public sealed record ArmtekAccount(string Key, string Name, string LogoFile, string ConfigSection, string HttpClientName);
