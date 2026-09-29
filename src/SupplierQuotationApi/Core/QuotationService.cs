using System.Diagnostics;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Infrastructure;
using SupplierQuotationApi.Providers;

namespace SupplierQuotationApi.Core;

public sealed class QuotationService
{
    private readonly IReadOnlyDictionary<string, IQuotationProvider> _providers;
    private readonly InflightResultCache _cache;
    private readonly AppOptions _options;
    private readonly ILogger<QuotationService> _logger;

    public QuotationService(IEnumerable<IQuotationProvider> providers, InflightResultCache cache,
        IOptions<AppOptions> options, ILogger<QuotationService> logger)
    {
        _providers = providers.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public IReadOnlyCollection<IQuotationProvider> Providers => _providers.Values.ToList();

    /// <summary>Ключи, которых нет в реестре.</summary>
    public IReadOnlyList<string> FindUnknown(IEnumerable<string>? keys) =>
        keys?.Where(k => !_providers.ContainsKey(k)).ToList() ?? [];

    /// <summary>Дожидается всех поставщиков и возвращает общий ответ.</summary>
    public async Task<QuotationResponse> QuoteAsync(QuotationRequest request, string? logoBaseUrl,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var results = new List<ProviderQuotation>();
        await foreach (var result in QuoteStreamAsync(request, logoBaseUrl, cancellationToken))
            results.Add(result);

        return new QuotationResponse
        {
            Article = request.Article.Trim(),
            Brand = request.Brand?.Trim(),
            RequestedAtUtc = startedAt,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Providers = results.OrderBy(x => x.ProviderName, StringComparer.CurrentCultureIgnoreCase).ToList()
        };
    }

    /// <summary>Отдаёт результат каждого поставщика сразу по готовности, в порядке завершения.</summary>
    public async IAsyncEnumerable<ProviderQuotation> QuoteStreamAsync(QuotationRequest request, string? logoBaseUrl,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var selected = request.Providers is { Count: > 0 }
            ? request.Providers.Select(k => _providers[k]).Distinct().ToList()
            : _providers.Values.Where(x => x.IsEnabled).ToList();
        var search = new QuotationSearch(request.Article.Trim(), request.Brand?.Trim(), request.IncludeAnalogs);

        var pending = selected.Select(p => QuoteProviderAsync(p, search, logoBaseUrl, cancellationToken)).ToList();
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending);
            pending.Remove(done);
            yield return await done;
        }
    }

    private async Task<ProviderQuotation> QuoteProviderAsync(IQuotationProvider provider, QuotationSearch search,
        string? logoBaseUrl, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var status = QuotationStatus.Ok;
        string? error = null;
        IReadOnlyList<QuotationOffer> offers = [];

        if (!provider.IsEnabled)
        {
            status = QuotationStatus.Disabled;
            error = "Поставщик выключен: не заполнены настройки в .env.";
        }
        else
        {
            try
            {
                var key = $"{provider.Key}|{search.Article}|{search.Brand}|{search.IncludeAnalogs}".ToUpperInvariant();
                offers = await _cache
                    .GetOrFetchAsync(key, TimeSpan.FromSeconds(_options.ResultCacheSeconds),
                        TimeSpan.FromSeconds(_options.ProviderTimeoutSeconds),
                        ct => provider.SearchAsync(search, ct))
                    .WaitAsync(cancellationToken);
                if (offers.Count == 0) status = QuotationStatus.NoOffers;
            }
            catch (TimeoutException ex)
            {
                status = QuotationStatus.Timeout;
                error = ex.Message;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Проценка {Provider} завершилась ошибкой.", provider.Key);
                status = QuotationStatus.Error;
                error = ex.Message;
            }
        }

        _logger.LogInformation("Проценка {Provider}: {Status}, {Count} предложений, {Ms} мс.",
            provider.Key, status, offers.Count, stopwatch.ElapsedMilliseconds);

        return new ProviderQuotation
        {
            ProviderKey = provider.Key,
            ProviderName = provider.Name,
            LogoUrl = ResolveLogoUrl(provider.LogoFile, logoBaseUrl),
            AccountLogin = provider.AccountLogin,
            Status = status,
            Error = error,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Offers = offers
        };
    }

    /// <summary>Локальный файл из wwwroot/logos или готовая внешняя ссылка (http/https), если логотип хранится у поставщика.</summary>
    public static string? ResolveLogoUrl(string? logoFile, string? logoBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(logoFile)) return null;
        if (logoFile.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            logoFile.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return logoFile;
        return logoBaseUrl is null ? null : $"{logoBaseUrl}/logos/{logoFile}";
    }
}
