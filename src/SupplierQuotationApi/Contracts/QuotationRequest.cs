using System.ComponentModel.DataAnnotations;

namespace SupplierQuotationApi.Contracts;

/// <summary>Единый запрос проценки: один артикул сразу у нескольких поставщиков.</summary>
public sealed class QuotationRequest
{
    /// <summary>Артикул детали.</summary>
    [Required, StringLength(100, MinimumLength = 1)]
    public string Article { get; init; } = string.Empty;

    /// <summary>Производитель для уточнения поиска. Необязателен.</summary>
    [StringLength(100)]
    public string? Brand { get; init; }

    /// <summary>Ключи поставщиков. Пусто или null — опросить всех включённых.</summary>
    public IReadOnlyList<string>? Providers { get; init; }

    /// <summary>Включать кроссы и аналоги. По умолчанию только оригиналы запрошенного артикула.</summary>
    public bool IncludeAnalogs { get; init; }
}
