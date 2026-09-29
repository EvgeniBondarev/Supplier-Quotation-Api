using System.Text;
using Microsoft.OpenApi.Models;
using SupplierQuotationApi.Providers;

namespace SupplierQuotationApi.Infrastructure.Swagger;

public static class SwaggerConfig
{
    public const string QuotationTag = "Проценка";
    public const string ProvidersTag = "Поставщики";
    public const string ApiKeyScheme = "ApiKey";

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Supplier Quotation API",
                Version = "v1",
                Description = BuildDescription(),
                Contact = new OpenApiContact
                {
                    Name = "Supplier-Quotation-Api на GitHub",
                    Url = new Uri("https://github.com/EvgeniBondarev/Supplier-Quotation-Api")
                }
            });

            var xml = Path.Combine(AppContext.BaseDirectory, "SupplierQuotationApi.xml");
            if (File.Exists(xml)) options.IncludeXmlComments(xml, includeControllerXmlComments: true);

            options.TagActionsBy(api => [api.RelativePath?.EndsWith("providers", StringComparison.OrdinalIgnoreCase) == true ? ProvidersTag : QuotationTag]);
            options.SupportNonNullableReferenceTypes();
            options.UseAllOfToExtendReferenceSchemas();

            options.AddSecurityDefinition(ApiKeyScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = ApiKeyMiddleware.HeaderName,
                Description = "Ключ клиента сервиса (`APP__APIKEY` в `.env`). Без него все методы отвечают `401`. " +
                              "Нажмите **Authorize**, чтобы Swagger UI подставлял его в запросы."
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiKeyScheme } }] = []
            });

            options.SchemaFilter<ContractSchemaFilter>();
            options.SchemaFilter<QuotationStatusSchemaFilter>();
            options.OperationFilter<QuotationOperationFilter>();
            options.ParameterFilter<ProviderKeyParameterFilter>();
            options.DocumentFilter<DocumentInfoFilter>();
        });
        return services;
    }

    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(ui =>
        {
            ui.DocumentTitle = "Supplier Quotation API";
            ui.SwaggerEndpoint("/swagger/v1/swagger.json", "Supplier Quotation API v1");
            ui.DisplayRequestDuration();
            ui.EnablePersistAuthorization();
            ui.DefaultModelsExpandDepth(1);
            ui.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        });
        return app;
    }

    /// <summary>Текст шапки документа: суть сервиса, статусы, особенности и таблица поставщиков из каталога.</summary>
    public static string BuildDescription()
    {
        var text = new StringBuilder();
        text.AppendLine("""
            Сервис проценки поставщиков автозапчастей. Один запрос с артикулом и брендом опрашивает поставщиков **параллельно**
            и возвращает предложения в **единой структуре**: цена в валюте поставщика и в рублях, остаток, склад, срок, кратность,
            логотип поставщика и логин учётной записи, под которой сделана проценка.

            ## Как пользоваться

            1. Передайте ключ в заголовке `X-Api-Key` (кнопка **Authorize**).
            2. `POST /api/quotations` — ответ одним JSON, либо `POST /api/quotations/stream` — поток NDJSON по мере готовности.
            3. **Не хотите ждать всех** — спросите поставщика отдельно: `POST /api/quotations/providers/{providerKey}`
               (или `GET` с параметрами в адресе). Вызовите его для каждого поставщика параллельно и показывайте ответы по мере прихода.
               Кэш и очередь ZZap общие с общей проценкой.
            4. Список поставщиков и их особенности — `GET /api/quotations/providers`.
            5. Проверка живости — `GET /health` (без ключа).

            ## Что важно знать

            - **Ошибка одного поставщика не ломает ответ**: она приходит внутри `providers[]` со статусом `Error`, `Timeout` или `Disabled`.
              Поэтому HTTP-код `200` не означает, что все поставщики ответили: смотрите `status` каждого.
            - **Таймаут** на поставщика — 20 секунд. Общее время равно времени самого медленного поставщика, а не сумме.
            - **Кэш** результатов — 60 секунд по ключу «поставщик + артикул + бренд + аналоги»; одинаковые одновременные запросы делают один вызов поставщику.
            - **Артикул**: разделители и регистр не важны (`K1223A` = `K 1223A`).
            - **Бренд** сопоставляется по алиасам производителей из базы Studio2 (`Kayaba` = `KYB`) и по вариантам написания (`WYNN'S` = `WYNNS`).
              Для ML-Auto, Микадо и ZZap бренд обязателен.
            - **Аналоги**: по умолчанию только оригиналы запрошенного артикула. Флаг `includeAnalogs` учитывают не все поставщики.
            - **Цены**: `price` — в валюте поставщика, `priceRub` — в рублях по курсу ЦБ РФ (кэш 1 час). Сортировка предложений — по цене, от дешёвых.
            - **Сроки** `deliveryDaysMin/Max`: `0` — товар на складе, `null` — срок не указан.
            - **Остаток**: `stock` — число (если удалось разобрать), `stockText` — исходная запись поставщика (`>10`, `под заказ`).
            - **Ограничения по IP**: МоТехС и Микадо работают только с адреса из белого списка клиента.
            - **ZZap**: не чаще 1 запроса в 3,5 с на весь ключ, поэтому отвечает по очереди; его подпись источника (`priceNote`) нужно показывать пользователю.

            ## Поставщики
            """);
        text.AppendLine();
        text.AppendLine("| Ключ | Валюта | Бренд обязателен | Аналоги | Доступ по IP | Протокол |");
        text.AppendLine("|---|---|---|---|---|---|");
        foreach (var (key, meta) in ProviderCatalog.All.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"| `{key}` | {meta.Currency} | {(meta.BrandRequired ? "да" : "нет")} | {(meta.SupportsAnalogs ? "да" : "нет")} | " +
                            $"{(meta.IpWhitelist ? "белый список" : "—")} | {meta.Protocol} |");
        text.AppendLine();
        text.AppendLine("Подробности по каждому — в ответе `GET /api/quotations/providers` (поля `notes`, `docsUrl`, `rateLimit`).");
        return text.ToString();
    }
}
