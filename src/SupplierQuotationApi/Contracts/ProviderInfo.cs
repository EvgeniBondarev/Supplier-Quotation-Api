namespace SupplierQuotationApi.Contracts;

/// <summary>Поставщик и его особенности: как устроен API и что нужно учитывать клиенту.</summary>
public sealed class ProviderInfo
{
    /// <summary>Ключ для поля <c>providers</c> запроса.</summary>
    public required string Key { get; init; }

    /// <summary>Название для показа.</summary>
    public required string Name { get; init; }

    /// <summary><c>true</c> — настройки заполнены, поставщик участвует в проценке по умолчанию.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Логин учётной записи, под которой идут запросы (для API без логина — метка).</summary>
    public string? AccountLogin { get; init; }

    /// <summary>Ссылка на логотип.</summary>
    public string? LogoUrl { get; init; }

    /// <summary>Транспорт и формат API поставщика.</summary>
    public string? Protocol { get; init; }

    /// <summary>Способ авторизации сервиса у поставщика.</summary>
    public string? Authentication { get; init; }

    /// <summary>Валюта цен поставщика (<c>price.currency</c> в предложениях).</summary>
    public string? Currency { get; init; }

    /// <summary>Без бренда поставщик возвращает статус <c>Error</c>.</summary>
    public bool BrandRequired { get; init; }

    /// <summary>Учитывает <c>includeAnalogs</c>; иначе всегда только оригиналы.</summary>
    public bool SupportsAnalogs { get; init; }

    /// <summary>Доступ ограничен по IP клиента на стороне поставщика.</summary>
    public bool IpWhitelist { get; init; }

    /// <summary>Ограничение частоты запросов, если есть.</summary>
    public string? RateLimit { get; init; }

    /// <summary>Особенности, важные для клиента.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Официальная документация поставщика, если ссылка известна.</summary>
    public string? DocsUrl { get; init; }

    /// <summary>Адрес API поставщика.</summary>
    public string? ApiUrl { get; init; }
}
