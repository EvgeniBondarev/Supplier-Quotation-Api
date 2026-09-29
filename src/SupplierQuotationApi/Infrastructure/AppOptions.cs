namespace SupplierQuotationApi.Infrastructure;

/// <summary>Настройки сервиса (APP__*).</summary>
public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>Ключ клиентов в заголовке X-Api-Key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Таймаут ответа одного поставщика.</summary>
    public int ProviderTimeoutSeconds { get; set; } = 20;

    /// <summary>Время жизни кэша результата проценки. 0 — кэш выключен.</summary>
    public int ResultCacheSeconds { get; set; } = 60;

    /// <summary>Открыть Swagger UI и OpenAPI-документ вне Development. Документ секретов не содержит, но раскрывает список поставщиков.</summary>
    public bool SwaggerEnabled { get; set; }
}
