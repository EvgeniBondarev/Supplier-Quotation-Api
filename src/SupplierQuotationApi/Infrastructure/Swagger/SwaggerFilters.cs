using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Providers;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SupplierQuotationApi.Infrastructure.Swagger;

/// <summary>Схемы контракта: примеры и перечисление ключей поставщиков.</summary>
public sealed class ContractSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var type = context.Type;

        if (type == typeof(QuotationRequest)) schema.Example = OpenApiAnyConverter.From(SwaggerExamples.RequestByBrand, ignoreNulls: true);
        else if (type == typeof(QuotationResponse)) schema.Example = OpenApiAnyConverter.From(SwaggerExamples.ResponseOk);
        else if (type == typeof(ProviderQuotation)) schema.Example = OpenApiAnyConverter.From(SwaggerExamples.ProviderOk);
        else if (type == typeof(ProviderInfo)) schema.Example = OpenApiAnyConverter.From(SwaggerExamples.ProviderInfoShateM);
        else if (type == typeof(Money)) schema.Example = OpenApiAnyConverter.From(new Money(82.62m, "BYN"));

        // Поле providers запроса: ключи — из каталога, чтобы Swagger UI показывал допустимые значения.
        if (type == typeof(QuotationRequest) && schema.Properties.TryGetValue("providers", out var providers))
        {
            providers.Items.Enum = ProviderCatalog.All.Keys
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Select(k => (IOpenApiAny)new OpenApiString(k)).ToList();
            providers.Nullable = true;
        }

        // Флаги качества, которые не выводятся из типов.
        if (type == typeof(QuotationOffer))
        {
            schema.Description = (schema.Description ?? string.Empty) +
                "\n\nПоля `price` и `priceRub` — цена за единицу. `stock` и `deliveryDays*` могут быть `null`: значение не указано поставщиком.";
        }
    }
}

/// <summary>Расшифровка значений <see cref="QuotationStatus"/> в схеме: Swagger UI показывает смысл каждого статуса.</summary>
public sealed class QuotationStatusSchemaFilter : ISchemaFilter
{
    private static readonly (string Value, string Meaning)[] Meanings =
    [
        ("Ok", "Есть хотя бы одно предложение."),
        ("NoOffers", "Запрос выполнен, предложений нет."),
        ("Error", "Ошибка поставщика или запроса; причина в поле `error` (нет доступа, нужен бренд, превышена квота и т. д.)."),
        ("Disabled", "Поставщика запросили явно, но его настройки не заполнены."),
        ("Timeout", "Поставщик не ответил за отведённое время (по умолчанию 20 с).")
    ];

    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(QuotationStatus)) return;

        schema.Description = "Итог опроса одного поставщика:\n\n" +
                             string.Join("\n", Meanings.Select(m => $"- `{m.Value}` — {m.Meaning}"));
        schema.Extensions["x-enumDescriptions"] = new OpenApiObject
        {
            [nameof(QuotationStatus.Ok)] = new OpenApiString(Meanings[0].Meaning),
            [nameof(QuotationStatus.NoOffers)] = new OpenApiString(Meanings[1].Meaning),
            [nameof(QuotationStatus.Error)] = new OpenApiString(Meanings[2].Meaning),
            [nameof(QuotationStatus.Disabled)] = new OpenApiString(Meanings[3].Meaning),
            [nameof(QuotationStatus.Timeout)] = new OpenApiString(Meanings[4].Meaning)
        };
    }
}

/// <summary>Примеры запросов и ответов на операциях, а также ответ NDJSON у стриминга.</summary>
public sealed class QuotationOperationFilter : IOperationFilter
{
    private const string Json = "application/json";
    private const string Ndjson = "application/x-ndjson";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;
        var isPost = string.Equals(context.ApiDescription.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase);

        if (isPost && path.EndsWith("quotations", StringComparison.OrdinalIgnoreCase))
        {
            AddRequestExamples(operation);
            AddResponseExamples(operation, "200", Json, new()
            {
                ["ok"] = Example("Несколько поставщиков с предложениями", "Каждый поставщик — отдельный элемент `providers`; цены поставщиков в BYN пересчитаны в `priceRub`.", SwaggerExamples.ResponseOk),
                ["mixed"] = Example("Разные исходы в одном ответе", "Ошибка, таймаут и выключенный поставщик приходят внутри ответа и не ломают остальных.", SwaggerExamples.ResponseMixed)
            });
        }
        else if (isPost && path.EndsWith("stream", StringComparison.OrdinalIgnoreCase))
        {
            AddRequestExamples(operation);
            AddNdjsonResponse(operation);
            KeepOnly(operation, "200", Ndjson);     // успешный ответ — только поток
            KeepOnly(operation, "400", Json);       // ошибки приходят до начала потока обычным JSON
            KeepOnly(operation, "401", Json);
        }
        else if (path.EndsWith("providers", StringComparison.OrdinalIgnoreCase))
        {
            AddResponseExamples(operation, "200", Json, new()
            {
                ["list"] = Example("Список поставщиков", "Паспорт каждого: протокол, валюта, обязательность бренда, ограничения.", SwaggerExamples.ProviderList)
            });
        }

        AddErrorExamples(operation);
    }

    /// <summary>Атрибут Produces применяется ко всем ответам метода; оставляем у ответа только нужный тип содержимого.</summary>
    private static void KeepOnly(OpenApiOperation operation, string code, string contentType)
    {
        if (!operation.Responses.TryGetValue(code, out var response)) return;
        foreach (var key in response.Content.Keys.Where(k => k != contentType).ToList()) response.Content.Remove(key);
    }

    private static void AddRequestExamples(OpenApiOperation operation)
    {
        if (operation.RequestBody?.Content.TryGetValue(Json, out var media) != true) return;
        media!.Examples = new Dictionary<string, OpenApiExample>
        {
            ["byBrand"] = Example("Артикул и бренд, все поставщики",
                "Основной сценарий: без `providers` опрашиваются все включённые поставщики.", SwaggerExamples.RequestByBrand),
            ["selected"] = Example("Выбранные поставщики",
                "Бренд `WYNN'S` найдётся и там, где у поставщика он записан как `WYNNS`.", SwaggerExamples.RequestSelected),
            ["withAnalogs"] = Example("С аналогами",
                "Флаг учитывают Шате-М, Армтек, Фаворит (с брендом), Форум-Авто и Берг. Аналоги помечены `providerData.isAnalog`.", SwaggerExamples.RequestWithAnalogs),
            ["noBrand"] = Example("Без бренда",
                "ML-Auto, Микадо и ZZap ответят статусом `Error` «укажите бренд», остальные вернут предложения всех брендов артикула.", SwaggerExamples.RequestNoBrand)
        };
    }

    private static void AddResponseExamples(OpenApiOperation operation, string code, string contentType, Dictionary<string, OpenApiExample> examples)
    {
        if (!operation.Responses.TryGetValue(code, out var response) || !response.Content.TryGetValue(contentType, out var media)) return;
        media.Examples = examples;
    }

    private static void AddNdjsonResponse(OpenApiOperation operation)
    {
        // Каждая строка потока — объект ProviderQuotation, порядок строк — порядок готовности поставщиков.
        var lines = string.Join("\n", new[]
        {
            SwaggerExamples.ResponseOk.Providers[0], SwaggerExamples.ResponseOk.Providers[1], SwaggerExamples.ResponseOk.Providers[2]
        }.Select(p => System.Text.Json.JsonSerializer.Serialize(p, ExampleJson)));

        if (!operation.Responses.TryGetValue("200", out var response)) return;
        response.Description = "Поток NDJSON: одна строка JSON на поставщика (объект `ProviderQuotation`), строки идут в порядке готовности. " +
                               "Первые цены приходят через сотни миллисекунд, не дожидаясь самых медленных поставщиков.";
        if (!response.Content.TryGetValue(Ndjson, out var media)) return;
        media.Examples = new Dictionary<string, OpenApiExample>
        {
            ["stream"] = new()
            {
                Summary = "Три строки потока",
                Description = "Ниже показаны три строки; в реальном ответе каждая — одна строка без переносов внутри.",
                Value = new OpenApiString(lines)
            }
        };
    }

    private static readonly System.Text.Json.JsonSerializerOptions ExampleJson = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static void AddErrorExamples(OpenApiOperation operation)
    {
        if (operation.Responses.TryGetValue("400", out var bad))
            foreach (var media in bad.Content.Values)
                media.Examples = new Dictionary<string, OpenApiExample>
                {
                    ["unknownProvider"] = Example("Неизвестный ключ поставщика", "Допустимые ключи — в `GET /api/quotations/providers`.", SwaggerExamples.BadRequest)
                };
        if (operation.Responses.TryGetValue("401", out var unauthorized))
            foreach (var media in unauthorized.Content.Values)
                media.Examples = new Dictionary<string, OpenApiExample>
                {
                    ["noKey"] = Example("Нет или неверен ключ", "Передайте заголовок `X-Api-Key`.", SwaggerExamples.Unauthorized)
                };
    }

    private static OpenApiExample Example(string summary, string description, object value) => new()
    {
        Summary = summary, Description = description, Value = OpenApiAnyConverter.From(value, ignoreNulls: value is QuotationRequest)
    };
}

/// <summary>Описание тегов и списка поставщиков в шапке документа.</summary>
public sealed class DocumentInfoFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        document.Tags =
        [
            new OpenApiTag
            {
                Name = SwaggerConfig.QuotationTag,
                Description = "Проценка артикула у поставщиков. Один запрос — ответы всех поставщиков в единой структуре."
            },
            new OpenApiTag
            {
                Name = SwaggerConfig.ProvidersTag,
                Description = "Справочник подключённых поставщиков: включён ли, какой у него API, что нужно учитывать клиенту."
            }
        ];
    }
}
