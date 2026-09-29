namespace SupplierQuotationApi.Contracts;

public sealed class QuotationResponse
{
    public required string Article { get; init; }
    public string? Brand { get; init; }
    public required DateTime RequestedAtUtc { get; init; }
    public required long DurationMs { get; init; }
    public required IReadOnlyList<ProviderQuotation> Providers { get; init; }
}

/// <summary>Результат одного поставщика. Ошибка одного не ломает ответы остальных.</summary>
public sealed class ProviderQuotation
{
    public required string ProviderKey { get; init; }
    public required string ProviderName { get; init; }
    public string? LogoUrl { get; init; }
    /// <summary>Логин учётной записи, под которой выполнена проценка.</summary>
    public string? AccountLogin { get; init; }
    public required QuotationStatus Status { get; init; }
    public string? Error { get; init; }
    public required long DurationMs { get; init; }
    public required IReadOnlyList<QuotationOffer> Offers { get; init; }
}

public enum QuotationStatus
{
    Ok,
    NoOffers,
    Error,
    Disabled,
    Timeout
}
