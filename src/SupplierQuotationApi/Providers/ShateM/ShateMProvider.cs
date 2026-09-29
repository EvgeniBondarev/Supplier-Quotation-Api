using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core;

namespace SupplierQuotationApi.Providers.ShateM;

public sealed class ShateMProvider : IQuotationProvider
{
    private const string LocationsCacheKey = "shatem:locations";
    private readonly IServiceProvider _services;
    private readonly ShateMOptions _options;
    private readonly ICurrencyConverter _currency;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;

    public ShateMProvider(IServiceProvider services, IOptions<ShateMOptions> options, ICurrencyConverter currency,
        IMemoryCache cache, TimeProvider clock)
    {
        _services = services;
        _options = options.Value;
        _currency = currency;
        _cache = cache;
        _clock = clock;
    }

    public string Key => "ShateM";
    public string Name => "Шате-М";
    public string? LogoFile => "shatem.svg";
    public string? AccountLogin => _options.Login;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Клиент транзитный: берём свежий из фабрики, чтобы работала ротация обработчиков.
        var client = _services.GetRequiredService<ShateMClient>();

        var articles = MatchArticles(await client.SearchArticlesAsync(Normalize(search.Article), cancellationToken), search);
        if (articles.Count == 0) return [];

        var locationsTask = GetLocationsAsync(client, cancellationToken);
        var pricesTask = client.SearchPricesAsync(articles.Select(x => x.Id), search.IncludeAnalogs, cancellationToken);
        await Task.WhenAll(locationsTask, pricesTask);

        var locations = locationsTask.Result;
        var now = _clock.GetUtcNow();
        var offers = new List<QuotationOffer>();
        foreach (var group in pricesTask.Result)
        foreach (var price in group.Prices ?? [])
        {
            // Аналоги приходят с другим articleId: без includeAnalogs оставляем только исходные артикулы.
            if (!search.IncludeAnalogs && price.ArticleId != group.Article.Id) continue;

            var source = price.Price?.Value;
            var currency = string.IsNullOrWhiteSpace(price.Price?.CurrencyCode) ? ShateMMapper.DefaultCurrency : price.Price!.CurrencyCode!;
            var rub = source is null ? null : await _currency.ToRubAsync(source.Value, currency, cancellationToken);
            if (ShateMMapper.Map(group.Article, price, locations, now, rub) is { } offer) offers.Add(offer);
        }

        return offers.OrderBy(x => x.PriceRub?.Amount ?? x.Price.Amount).ToList();
    }

    /// <summary>Артикул должен совпасть с запрошенным; бренд — если указан (точно, иначе по вхождению: MAHLE ↔ MAHLE ORIGINAL).</summary>
    private static List<ShateMArticle> MatchArticles(IEnumerable<ShateMArticle> found, QuotationSearch search)
    {
        var code = Normalize(search.Article);
        var byCode = found.Where(x => Normalize(x.Code) == code).ToList();
        if (string.IsNullOrWhiteSpace(search.Brand)) return byCode;

        var brand = Normalize(search.Brand);
        var exact = byCode.Where(x => Normalize(x.TradeMarkName) == brand).ToList();
        return exact.Count > 0
            ? exact
            : byCode.Where(x => Normalize(x.TradeMarkName) is { Length: > 0 } b && (b.Contains(brand) || brand.Contains(b))).ToList();
    }

    private async Task<IReadOnlyDictionary<string, ShateMLocation>> GetLocationsAsync(ShateMClient client, CancellationToken ct)
    {
        if (_cache.TryGetValue(LocationsCacheKey, out IReadOnlyDictionary<string, ShateMLocation>? cached) && cached is not null)
            return cached;

        try
        {
            var locations = (await client.GetLocationsAsync(ct))
                .Where(x => !string.IsNullOrWhiteSpace(x.Code))
                .GroupBy(x => x.Code!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            _cache.Set(LocationsCacheKey, (IReadOnlyDictionary<string, ShateMLocation>)locations, TimeSpan.FromMinutes(30));
            return locations;
        }
        catch (HttpRequestException)
        {
            // Справочник — украшение: без него склад показывается кодом.
            return new Dictionary<string, ShateMLocation>();
        }
    }

    private static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
