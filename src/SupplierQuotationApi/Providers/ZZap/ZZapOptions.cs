namespace SupplierQuotationApi.Providers.ZZap;

/// <summary>ZZap Москва: SUPPLIERS__ZZAP__*.</summary>
public sealed class ZZapOptions
{
    public const string Section = "Suppliers:ZZap";

    public string? BaseUrl { get; set; } = "https://b52-api.zzap.pro";
    public string? ApiKey { get; set; }
    /// <summary>Регион поиска: 1 — Москва и область. Задаётся в каждом запросе, регион аккаунта на поиск не влияет.</summary>
    public int CodeRegion { get; set; } = 1;
    /// <summary>Пауза между запросами: по правилам ZZap не чаще 1 запроса в 3 секунды, на превышение — 429 и капча.</summary>
    public int MinRequestIntervalMs { get; set; } = 3500;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}
