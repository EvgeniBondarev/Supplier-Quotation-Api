using System.ComponentModel.DataAnnotations;

namespace SupplierQuotationApi.Contracts;

/// <summary>Единый запрос проценки: один артикул сразу у нескольких поставщиков.</summary>
public sealed class QuotationRequest
{
    /// <summary>
    /// Артикул детали. Разделители и регистр значения не имеют: <c>K1223A</c>, <c>K 1223A</c> и <c>k-1223a</c> — один и тот же артикул.
    /// </summary>
    /// <example>K1223A</example>
    [Required, StringLength(100, MinimumLength = 1)]
    public string Article { get; init; } = string.Empty;

    /// <summary>
    /// Производитель. Написание может отличаться от каталога поставщика (<c>Kayaba</c> и <c>KYB</c>, <c>WYNN'S</c> и <c>WYNNS</c>):
    /// сервис приводит его сам по алиасам и вариантам написания.
    /// <para><b>Обязателен</b> для ML-Auto (BY и RU), Микадо и ZZap: без бренда они возвращают статус <c>Error</c>.
    /// Остальные без бренда отдают предложения всех брендов артикула.</para>
    /// </summary>
    /// <example>Filtron</example>
    [StringLength(100)]
    public string? Brand { get; init; }

    /// <summary>
    /// Ключи поставщиков (см. <c>GET /api/quotations/providers</c>). Пусто или не указано — опрашиваются все включённые.
    /// Если указан выключенный поставщик (не заполнены его настройки), он вернётся со статусом <c>Disabled</c>.
    /// Неизвестный ключ — ответ <c>400</c>.
    /// </summary>
    public IReadOnlyList<string>? Providers { get; init; }

    /// <summary>
    /// Включать кроссы и аналоги. По умолчанию только оригиналы запрошенного артикула.
    /// Флаг учитывают Шате-М, Армтек, Фаворит (только вместе с брендом), Форум-Авто и Берг; остальные всегда отдают только оригиналы
    /// (<c>supportsAnalogs</c> в списке поставщиков). Аналоги помечены <c>providerData.isAnalog = "true"</c>.
    /// </summary>
    /// <example>false</example>
    public bool IncludeAnalogs { get; init; }
}
