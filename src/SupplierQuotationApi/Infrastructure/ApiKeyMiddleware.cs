using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Infrastructure;

/// <summary>Пускает только запросы с верным X-Api-Key. /health и Swagger открыты.</summary>
public sealed class ApiKeyMiddleware
{
    public const string HeaderName = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly byte[]? _expected;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<AppOptions> options)
    {
        _next = next;
        var key = options.Value.ApiKey;
        _expected = string.IsNullOrEmpty(key) ? null : Encoding.UTF8.GetBytes(key);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_expected is null || IsPublic(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var provided = context.Request.Headers[HeaderName].ToString();
        if (!string.IsNullOrEmpty(provided) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), _expected))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { title = $"Нужен корректный заголовок {HeaderName}.", status = 401 });
    }

    private static bool IsPublic(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/swagger") || path.StartsWithSegments("/logos");
}
