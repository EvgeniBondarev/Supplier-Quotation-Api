using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Nikei;

public sealed class NikeiProvider : IQuotationProvider
{
    private const int MaxBrands = 6;
    private readonly IServiceProvider _services;
    private readonly NikeiOptions _options;
    private readonly IProducerAliasService _aliases;

    public NikeiProvider(IServiceProvider services, IOptions<NikeiOptions> options, IProducerAliasService aliases)
    {
        _services = services;
        _options = options.Value;
        _aliases = aliases;
    }

    public string Key => "Nikei";
    public string Name => "Nikei";
    public string? LogoFile => "nikei.svg";
    public string? AccountLogin => _options.Login;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Клиент транзитный: свежий из фабрики, чтобы работала ротация обработчиков.
        var client = _services.GetRequiredService<NikeiClient>();
        var aliases = await _aliases.GetMapAsync(cancellationToken);

        var parts = new List<NikeiPart>();
        if (!string.IsNullOrWhiteSpace(search.Brand))
        {
            // Nikei сопоставляет бренд нестрого («Febi» находит «FEBI BILSTEIN»): пробуем исходное написание и алиасы.
            var responses = await Task.WhenAll(aliases.QueryVariants(search.Brand, 3)
                .Select(b => client.SearchAsync(search.Article, b, cancellationToken)));
            parts = responses.SelectMany(x => x).ToList();
        }

        // Бренда нет или под ним пусто: без бренда Nikei отдаёт список брендов номера — берём подходящие и ищем по каждому.
        if (parts.Count == 0)
        {
            var brands = (await client.GetBrandsAsync(search.Article, cancellationToken))
                .Where(b => aliases.Matches(b, search.Brand)).Take(MaxBrands).ToList();
            var responses = await Task.WhenAll(brands.Select(b => client.SearchAsync(search.Article, b, cancellationToken)));
            parts = responses.SelectMany(x => x).ToList();
        }

        return NikeiMapper.MapOriginals(parts, search.Article, search.Brand, aliases)
            .OrderBy(x => x.Price.Amount)
            .ToList();
    }
}
