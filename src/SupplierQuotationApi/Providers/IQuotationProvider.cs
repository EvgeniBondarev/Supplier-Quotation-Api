using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers;

/// <summary>Единый контракт поставщика. Одна реализация — один API и одна учётная запись.</summary>
public interface IQuotationProvider
{
    /// <summary>Стабильный ключ поставщика (Armtek, ShateM, ...).</summary>
    string Key { get; }
    string Name { get; }
    /// <summary>Имя файла логотипа в wwwroot/logos, без пути. null — логотипа нет.</summary>
    string? LogoFile { get; }
    /// <summary>Логин учётной записи, под которой идут запросы.</summary>
    string? AccountLogin { get; }
    bool IsEnabled { get; }

    Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken);
}

public sealed record QuotationSearch(string Article, string? Brand, bool IncludeAnalogs);
