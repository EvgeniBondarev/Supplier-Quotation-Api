namespace SupplierQuotationApi.Contracts;

/// <summary>Результат проценки: ответы всех опрошенных поставщиков.</summary>
public sealed class QuotationResponse
{
    /// <summary>Артикул из запроса (без пробелов по краям).</summary>
    public required string Article { get; init; }

    /// <summary>Бренд из запроса, если был указан.</summary>
    public string? Brand { get; init; }

    /// <summary>Момент начала обработки запроса, UTC.</summary>
    public required DateTime RequestedAtUtc { get; init; }

    /// <summary>Полное время обработки, мс. Равно времени самого медленного поставщика, а не сумме.</summary>
    public required long DurationMs { get; init; }

    /// <summary>Ответы поставщиков, отсортированные по названию. Ошибка одного не ломает остальных.</summary>
    public required IReadOnlyList<ProviderQuotation> Providers { get; init; }
}

/// <summary>Результат одного поставщика: статус, время ответа и предложения.</summary>
public sealed class ProviderQuotation
{
    /// <summary>Стабильный ключ поставщика для поля <c>providers</c> запроса.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>Название для показа пользователю.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Ссылка на логотип: файл этого сервиса (<c>/logos/…</c>) или адрес на сайте поставщика.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>
    /// Логин учётной записи, под которой выполнена проценка. Для API без логина — метка <c>api-key</c> или начало ключа
    /// (сам секрет не раскрывается).
    /// </summary>
    public string? AccountLogin { get; init; }

    /// <summary>Итог опроса поставщика.</summary>
    public required QuotationStatus Status { get; init; }

    /// <summary>Причина при статусах <c>Error</c>, <c>Timeout</c>, <c>Disabled</c>: текст для пользователя.</summary>
    public string? Error { get; init; }

    /// <summary>Время ответа этого поставщика, мс. Из кэша — близко к нулю.</summary>
    public required long DurationMs { get; init; }

    /// <summary>Предложения, от дешёвых к дорогим. Пусто при любом статусе, кроме <c>Ok</c>.</summary>
    public required IReadOnlyList<QuotationOffer> Offers { get; init; }
}

/// <summary>Итог опроса одного поставщика.</summary>
public enum QuotationStatus
{
    /// <summary>Есть хотя бы одно предложение.</summary>
    Ok,

    /// <summary>Запрос выполнен, предложений нет (товара нет в наличии или у поставщика).</summary>
    NoOffers,

    /// <summary>Ошибка поставщика или запроса: нет доступа, нужен бренд, превышена квота, неверный ответ. Текст в <c>error</c>.</summary>
    Error,

    /// <summary>Поставщика запросили явно, но его настройки в <c>.env</c> не заполнены.</summary>
    Disabled,

    /// <summary>Поставщик не ответил за отведённое время (по умолчанию 20 с).</summary>
    Timeout
}
