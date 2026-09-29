using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Berg;

public sealed class BergProvider : IQuotationProvider
{
    private const string AddressCacheKey = "berg:address";
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    private readonly IServiceProvider _services;
    private readonly BergOptions _options;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly IProducerAliasService _aliases;

    public BergProvider(IServiceProvider services, IOptions<BergOptions> options, IMemoryCache cache, TimeProvider clock,
        IProducerAliasService aliases)
    {
        _aliases = aliases;
        _services = services;
        _options = options.Value;
        _cache = cache;
        _clock = clock;
    }

    public string Key => "Berg";
    public string Name => "Берг";
    public string? LogoFile => "berg.png";
    /// <summary>У Berg нет логина, аккаунт задаёт ключ. Показываем только адрес отгрузки, секрет не раскрываем.</summary>
    public string? AccountLogin => string.IsNullOrWhiteSpace(_options.DeliveryAddressId) ? "api-key" : $"api-key, адрес {_options.DeliveryAddressId}";
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Клиент транзитный: свежий из фабрики, чтобы работала ротация обработчиков.
        var client = _services.GetRequiredService<BergClient>();

        var addressId = await ResolveAddressIdAsync(client, cancellationToken);
        var resources = await client.GetStockAsync(search.Article, search.IncludeAnalogs, addressId, cancellationToken);

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().ToOffset(MoscowOffset).DateTime);
        var aliases = await _aliases.GetMapAsync(cancellationToken);
        return BergMapper.Map(resources, search.Article, search.Brand, search.IncludeAnalogs, today, aliases)
            .OrderBy(x => x.Price.Amount)
            .ToList();
    }

    /// <summary>Адрес из настроек, иначе первый активный адрес аккаунта (кэш 30 минут).
    /// Сбой справочника не блокирует проценку: без адреса сроки берутся из гарантированного периода.</summary>
    private async Task<long?> ResolveAddressIdAsync(BergClient client, CancellationToken ct)
    {
        if (long.TryParse(_options.DeliveryAddressId, out var configured)) return configured;

        if (_cache.TryGetValue(AddressCacheKey, out long? cached)) return cached;
        try
        {
            var id = (await client.GetActiveAddressesAsync(ct)).FirstOrDefault(x => x.State == 1)?.Id;
            _cache.Set(AddressCacheKey, id, TimeSpan.FromMinutes(30));
            return id;
        }
        catch (Exception ex) when (ex is BergApiException or HttpRequestException)
        {
            return null;
        }
    }
}
