using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.ProfitLiga;

public sealed class ProfitLigaProvider : IQuotationProvider
{
    private readonly IServiceProvider _services;
    private readonly ProfitLigaOptions _options;
    private readonly IProducerAliasService _aliases;

    public ProfitLigaProvider(IServiceProvider services, IOptions<ProfitLigaOptions> options, IProducerAliasService aliases)
    {
        _services = services;
        _options = options.Value;
        _aliases = aliases;
    }

    public string Key => "ProfitLiga";
    public string Name => "Профит-Лига";
    public string? LogoFile => "profit-liga.svg";
    /// <summary>У Профит-Лиги нет логина, аккаунт задаёт ключ: сам ключ не раскрываем.</summary>
    public string? AccountLogin => "api-key";
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Клиент транзитный: свежий из фабрики, чтобы работала ротация обработчиков.
        var client = _services.GetRequiredService<ProfitLigaClient>();
        var aliases = await _aliases.GetMapAsync(cancellationToken);

        List<ProfitLigaCard> cards;
        if (string.IsNullOrWhiteSpace(search.Brand))
        {
            // Бренд неизвестен: /search/items отдаёт все бренды артикула с наличием.
            cards = await client.SearchItemsAsync(search.Article, cancellationToken);
        }
        else
        {
            // Фильтр бренда есть только у /search/crosses и он точный: пробуем исходное написание и алиасы.
            var responses = await Task.WhenAll(aliases.QueryVariants(search.Brand, 3)
                .Select(b => client.SearchCrossesAsync(search.Article, b, cancellationToken)));
            cards = responses.SelectMany(x => x).ToList();
        }

        return ProfitLigaMapper.MapOriginals(cards, search.Article, search.Brand, aliases)
            .OrderBy(x => x.Price.Amount)
            .ToList();
    }
}
