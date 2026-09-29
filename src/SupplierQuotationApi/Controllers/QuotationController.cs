using System.Net.Mime;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core;
using SupplierQuotationApi.Providers;

namespace SupplierQuotationApi.Controllers;

/// <summary>Проценка артикула у поставщиков автозапчастей и справочник поставщиков.</summary>
[ApiController]
[Route("api/quotations")]
public sealed class QuotationController : ControllerBase
{
    private readonly QuotationService _service;
    private readonly JsonSerializerOptions _json;

    public QuotationController(QuotationService service, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json)
    {
        _service = service;
        _json = json.Value.SerializerOptions;
    }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    /// <summary>Проценка артикула у поставщиков одним ответом.</summary>
    /// <remarks>
    /// Опрашивает выбранных (или всех включённых) поставщиков параллельно и возвращает ответ, когда отработали все:
    /// время ответа равно времени самого медленного поставщика (таймаут — 20 секунд).
    ///
    /// **HTTP 200 не означает, что все поставщики ответили.** Ошибка, таймаут или выключенный поставщик приходят внутри
    /// `providers[]` со своим `status` и текстом в `error`, остальные поставщики при этом отдают предложения как обычно.
    ///
    /// Предложения каждого поставщика отсортированы по цене. По умолчанию возвращаются только оригиналы запрошенного артикула;
    /// кроссы включаются флагом `includeAnalogs` у тех поставщиков, которые его поддерживают.
    ///
    /// Если ответ нужен как можно раньше, используйте `POST /api/quotations/stream`: он отдаёт поставщиков по мере готовности.
    /// </remarks>
    /// <param name="request">Артикул, бренд, необязательный список поставщиков и флаг аналогов.</param>
    /// <response code="200">Ответы всех опрошенных поставщиков. Проверяйте `status` каждого.</response>
    /// <response code="400">Пустой артикул или неизвестный ключ поставщика в `providers`.</response>
    /// <response code="401">Не передан заголовок `X-Api-Key` или он неверный.</response>
    [HttpPost]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType<QuotationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Quote([FromBody] QuotationRequest request, CancellationToken cancellationToken)
    {
        if (Unknown(request) is { } problem) return problem;
        return Ok(await _service.QuoteAsync(request, BaseUrl, cancellationToken));
    }

    /// <summary>Та же проценка потоком NDJSON: строка на поставщика, по мере готовности.</summary>
    /// <remarks>
    /// Запрос такой же, как у `POST /api/quotations`. Ответ — `application/x-ndjson`: каждая строка — отдельный JSON-объект
    /// `ProviderQuotation`, строки идут в **порядке готовности** поставщиков, а не по алфавиту. Первые цены приходят через
    /// сотни миллисекунд, не дожидаясь самых медленных поставщиков (например, ZZap отвечает по очереди).
    ///
    /// Соединение закрывается, когда отработал последний поставщик. Итоговых полей (`durationMs`, `requestedAtUtc`) в потоке нет.
    /// Ошибки запроса (`400`, `401`) приходят до начала потока обычным JSON.
    /// </remarks>
    /// <param name="request">Артикул, бренд, необязательный список поставщиков и флаг аналогов.</param>
    /// <response code="200">Поток NDJSON, по строке на поставщика.</response>
    /// <response code="400">Пустой артикул или неизвестный ключ поставщика в `providers`.</response>
    /// <response code="401">Не передан заголовок `X-Api-Key` или он неверный.</response>
    [HttpPost("stream")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces("application/x-ndjson", MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(ProviderQuotation), StatusCodes.Status200OK, "application/x-ndjson")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, MediaTypeNames.Application.Json)]
    public async Task Stream([FromBody] QuotationRequest request, CancellationToken cancellationToken)
    {
        if (Unknown(request) is { } problem)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(problem.Value, cancellationToken);
            return;
        }

        Response.ContentType = "application/x-ndjson";
        await foreach (var result in _service.QuoteStreamAsync(request, BaseUrl, cancellationToken))
        {
            await JsonSerializer.SerializeAsync(Response.Body, result, _json, cancellationToken);
            await Response.WriteAsync("\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    /// <summary>Проценка у одного поставщика, не дожидаясь остальных.</summary>
    /// <remarks>
    /// Возвращает результат одного поставщика (объект `ProviderQuotation`, такой же, как элемент `providers[]` общего ответа).
    /// Запрос нужен, когда результаты показываются по мере готовности: вызовите его для каждого поставщика параллельно
    /// и выводите ответы по мере прихода, не ожидая самого медленного (например, ZZap отвечает по очереди).
    ///
    /// Ключ поставщика не зависит от регистра. Кэш результатов и очередь ZZap общие с `POST /api/quotations`: если тот же артикул
    /// уже искали в последнюю минуту, ответ придёт мгновенно, а повторный общий запрос не пойдёт к поставщику второй раз.
    ///
    /// Как и в общей проценке, **HTTP 200 не означает наличия предложений**: смотрите `status`. Выключенный поставщик,
    /// таймаут и сбой приходят статусом `Disabled`, `Timeout`, `Error` с текстом в `error`.
    /// </remarks>
    /// <param name="providerKey">Ключ поставщика из `GET /api/quotations/providers`.</param>
    /// <param name="request">Артикул, бренд и флаг аналогов.</param>
    /// <response code="200">Результат поставщика. Проверяйте `status`.</response>
    /// <response code="400">Пустой артикул.</response>
    /// <response code="401">Не передан заголовок `X-Api-Key` или он неверный.</response>
    /// <response code="404">Такого поставщика нет.</response>
    [HttpPost("providers/{providerKey}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProviderQuotation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ProviderQuotation>> QuoteProvider(string providerKey, [FromBody] ProviderQuotationRequest request,
        CancellationToken cancellationToken) => QuoteSingleAsync(providerKey, request, cancellationToken);

    /// <summary>Проценка у одного поставщика: то же, что POST, параметры в адресе.</summary>
    /// <remarks>
    /// Удобно для проверки из браузера и для простых клиентов: `GET /api/quotations/providers/ShateM?article=K1223A&amp;brand=Filtron`.
    /// Поведение, кэш и ответы те же, что у `POST /api/quotations/providers/{providerKey}`.
    /// </remarks>
    /// <param name="providerKey">Ключ поставщика из `GET /api/quotations/providers`.</param>
    /// <param name="article">Артикул детали; разделители и регистр не важны (`K1223A` = `K 1223A`).</param>
    /// <param name="brand">Производитель. Обязателен для ML-Auto (BY и RU), Микадо и ZZap; написание может отличаться от каталога поставщика.</param>
    /// <param name="includeAnalogs">Включать кроссы и аналоги; учитывают не все поставщики (`supportsAnalogs`).</param>
    /// <response code="200">Результат поставщика. Проверяйте `status`.</response>
    /// <response code="400">Пустой артикул.</response>
    /// <response code="401">Не передан заголовок `X-Api-Key` или он неверный.</response>
    /// <response code="404">Такого поставщика нет.</response>
    [HttpGet("providers/{providerKey}")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProviderQuotation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ActionResult<ProviderQuotation>> QuoteProviderByQuery(
        string providerKey,
        [FromQuery, System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 1)] string article,
        [FromQuery, System.ComponentModel.DataAnnotations.StringLength(100)] string? brand,
        [FromQuery] bool includeAnalogs,
        CancellationToken cancellationToken) =>
        QuoteSingleAsync(providerKey, new ProviderQuotationRequest { Article = article, Brand = brand, IncludeAnalogs = includeAnalogs }, cancellationToken);

    private async Task<ActionResult<ProviderQuotation>> QuoteSingleAsync(string providerKey, ProviderQuotationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _service.QuoteSingleAsync(providerKey, request, BaseUrl, cancellationToken);
        return result is null
            ? NotFound(new ProblemDetails { Title = $"Неизвестный поставщик: {providerKey}", Status = StatusCodes.Status404NotFound })
            : Ok(result);
    }

    /// <summary>Подключённые поставщики и их особенности.</summary>
    /// <remarks>
    /// Для каждого поставщика: ключ для поля `providers`, включён ли он (заполнены ли настройки в `.env`), логин учётной записи,
    /// логотип, а также его паспорт — протокол, способ авторизации, валюта, обязателен ли бренд, поддерживаются ли аналоги,
    /// ограничения по IP и частоте запросов и заметки, важные для клиента.
    ///
    /// Выключенные поставщики в проценке по умолчанию не участвуют. Секреты в ответе не раскрываются.
    /// </remarks>
    /// <response code="200">Список поставщиков.</response>
    /// <response code="401">Не передан заголовок `X-Api-Key` или он неверный.</response>
    [HttpGet("providers")]
    [Produces(MediaTypeNames.Application.Json)]
    [ProducesResponseType<IReadOnlyList<ProviderInfo>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public ActionResult<IReadOnlyList<ProviderInfo>> Providers() => Ok(_service.Providers
        .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
        .Select(p =>
        {
            var meta = ProviderCatalog.Find(p.Key);
            return new ProviderInfo
            {
                Key = p.Key, Name = p.Name, IsEnabled = p.IsEnabled, AccountLogin = p.AccountLogin,
                LogoUrl = QuotationService.ResolveLogoUrl(p.LogoFile, BaseUrl),
                Protocol = meta?.Protocol, Authentication = meta?.Authentication, Currency = meta?.Currency,
                BrandRequired = meta?.BrandRequired ?? false, SupportsAnalogs = meta?.SupportsAnalogs ?? false,
                IpWhitelist = meta?.IpWhitelist ?? false, RateLimit = meta?.RateLimit, Notes = meta?.Notes ?? [],
                DocsUrl = meta?.DocsUrl, ApiUrl = meta?.ApiUrl
            };
        }).ToList());

    private BadRequestObjectResult? Unknown(QuotationRequest request)
    {
        var unknown = _service.FindUnknown(request.Providers);
        return unknown.Count == 0
            ? null
            : BadRequest(new ProblemDetails { Title = $"Неизвестные поставщики: {string.Join(", ", unknown)}", Status = StatusCodes.Status400BadRequest });
    }
}
