using System.ComponentModel.DataAnnotations;

namespace SupplierQuotationApi.Contracts;

/// <summary>
/// Запрос проценки у одного поставщика. Поставщик задаётся в адресе (<c>/providers/{providerKey}</c>),
/// поэтому списка <c>providers</c>, как у общего запроса, здесь нет.
/// </summary>
public sealed class ProviderQuotationRequest
{
    /// <summary>
    /// Артикул детали. Разделители и регистр значения не имеют: <c>K1223A</c>, <c>K 1223A</c> и <c>k-1223a</c> — один и тот же артикул.
    /// </summary>
    /// <example>K1223A</example>
    [Required, StringLength(100, MinimumLength = 1)]
    public string Article { get; init; } = string.Empty;

    /// <summary>
    /// Производитель. Написание может отличаться от каталога поставщика (<c>Kayaba</c> и <c>KYB</c>): сервис приводит его сам.
    /// <b>Обязателен</b> для ML-Auto (BY и RU), Микадо и ZZap: без бренда они вернут статус <c>Error</c>.
    /// </summary>
    /// <example>Filtron</example>
    [StringLength(100)]
    public string? Brand { get; init; }

    /// <summary>
    /// Включать кроссы и аналоги. Флаг учитывают не все поставщики (<c>supportsAnalogs</c> в списке поставщиков),
    /// остальные всегда отдают только оригиналы.
    /// </summary>
    /// <example>false</example>
    public bool IncludeAnalogs { get; init; }
}
