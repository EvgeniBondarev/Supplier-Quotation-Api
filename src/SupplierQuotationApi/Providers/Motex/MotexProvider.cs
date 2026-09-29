using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Motex;

public sealed class MotexProvider : IQuotationProvider
{
    private const string AddressCacheKey = "motex:delivery-address";
    private readonly IServiceProvider _services;
    private readonly MotexOptions _options;
    private readonly ICurrencyConverter _currency;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly IProducerAliasService _aliases;

    public MotexProvider(IServiceProvider services, IOptions<MotexOptions> options, ICurrencyConverter currency,
        IMemoryCache cache, TimeProvider clock, IProducerAliasService aliases)
    {
        _aliases = aliases;
        _services = services;
        _options = options.Value;
        _currency = currency;
        _cache = cache;
        _clock = clock;
    }

    public string Key => "Motex";
    public string Name => "МоТехС";
    public string? LogoFile => "motex.png";
    public string? AccountLogin => _options.Login;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Клиент транзитный: свежий из фабрики, чтобы работала ротация обработчиков.
        var client = _services.GetRequiredService<MotexClient>();

        var addressCode = await GetSingleAddressCodeAsync(client, cancellationToken);
        // Бренд уходит в API как есть: пробуем исходное написание и алиасы из базы Studio2, результаты объединяем.
        var aliases = await _aliases.GetMapAsync(cancellationToken);
        var brands = aliases.QueryVariants(search.Brand, 3);
        var responses = await Task.WhenAll((brands.Count == 0 ? [(string?)null] : brands.Select(b => (string?)b))
            .Select(b => client.SearchAsync(search.Article, b, addressCode, cancellationToken)));
        var articles = responses.SelectMany(x => x)
            .DistinctBy(x => string.Join("|", x.Code, x.StoreCode, x.Price)).ToList();

        var currency = string.IsNullOrWhiteSpace(_options.Currency) ? "BYN" : _options.Currency!.ToUpperInvariant();
        var now = _clock.GetUtcNow();
        var offers = new List<QuotationOffer>();
        // Контракт: только оригиналы запрошенного артикула — аналоги (isAnalog=1) отбрасываем.
        foreach (var article in articles.Where(x => (x.IsAnalog ?? 0) == 0))
        {
            var price = MotexMapper.ParseNumber(article.Price);
            var rub = price is null ? null : await _currency.ToRubAsync(price.Value, currency, cancellationToken);
            if (MotexMapper.Map(article, currency, now, rub) is { } offer) offers.Add(offer);
        }

        return offers.OrderBy(x => x.PriceRub?.Amount ?? x.Price.Amount).ToList();
    }

    /// <summary>deliveryAddressCode API требует, только если у клиента больше одного адреса; если адрес ровно один — подставляем его.</summary>
    private async Task<string?> GetSingleAddressCodeAsync(MotexClient client, CancellationToken ct)
    {
        if (_cache.TryGetValue(AddressCacheKey, out string? cached)) return cached;
        try
        {
            var codes = (await client.GetDeliveryAddressesAsync(ct)).Select(x => x.Code?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var code = codes.Count == 1 ? codes[0] : null;
            _cache.Set(AddressCacheKey, code, TimeSpan.FromMinutes(30));
            return code;
        }
        catch (Exception ex) when (ex is MotexApiException or HttpRequestException)
        {
            // Справочник вспомогательный: без него ищем без адреса, ошибку авторизации/доступа покажет сам поиск.
            return null;
        }
    }
}
