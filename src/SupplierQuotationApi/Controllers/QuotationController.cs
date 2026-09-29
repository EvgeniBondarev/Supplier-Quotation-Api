using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core;

namespace SupplierQuotationApi.Controllers;

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

    /// <summary>Проценка артикула у выбранных (или всех включённых) поставщиков одним ответом.</summary>
    [HttpPost]
    [ProducesResponseType<QuotationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Quote([FromBody] QuotationRequest request, CancellationToken cancellationToken)
    {
        if (Unknown(request) is { } problem) return problem;
        return Ok(await _service.QuoteAsync(request, BaseUrl, cancellationToken));
    }

    /// <summary>Та же проценка потоком NDJSON: строка на поставщика, по мере готовности.</summary>
    [HttpPost("stream")]
    [Produces("application/x-ndjson")]
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

    /// <summary>Список подключённых поставщиков.</summary>
    [HttpGet("providers")]
    public IActionResult Providers() => Ok(_service.Providers.Select(p => new
    {
        p.Key, p.Name, p.IsEnabled, p.AccountLogin,
        LogoUrl = p.LogoFile is null ? null : $"{BaseUrl}/logos/{p.LogoFile}"
    }));

    private BadRequestObjectResult? Unknown(QuotationRequest request)
    {
        var unknown = _service.FindUnknown(request.Providers);
        return unknown.Count == 0
            ? null
            : BadRequest(new ProblemDetails { Title = $"Неизвестные поставщики: {string.Join(", ", unknown)}" });
    }
}
