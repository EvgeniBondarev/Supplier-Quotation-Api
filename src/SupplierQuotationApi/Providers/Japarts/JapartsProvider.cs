using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Japarts;

public sealed class JapartsProvider : IQuotationProvider
{
    private readonly IServiceProvider _services;
    private readonly JapartsOptions _options;
    private readonly IProducerAliasService _aliases;

    public JapartsProvider(IServiceProvider services, IOptions<JapartsOptions> options, IProducerAliasService aliases)
    {
        _services = services;
        _options = options.Value;
        _aliases = aliases;
    }

    public string Key => "Japarts";
    public string Name => "Japarts";
    public string? LogoFile => "japarts.jpg";
    public string? AccountLogin => _options.Login;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Клиент транзитный: свежий из фабрики, чтобы работала ротация обработчиков.
        var client = _services.GetRequiredService<JapartsClient>();
        var aliases = await _aliases.GetMapAsync(cancellationToken);

        // С брендом пробуем исходное написание и алиасы (только безопасные для SQL Japarts), результаты объединяем.
        var brands = aliases.QueryVariants(search.Brand, 3).Where(JapartsClient.IsSafeParameter).ToList();
        var offers = new List<JapartsOffer>();
        if (brands.Count > 0)
            offers = (await Task.WhenAll(brands.Select(b => client.SearchAsync(search.Article, b, cancellationToken))))
                .SelectMany(x => x).ToList();

        // Бренд не задан, у него небезопасные символы или Japarts называет его иначе («WYNN'S» — «Wynn’s»): берём все бренды
        // номера и отбираем нужный локально по алиасам.
        if (offers.Count == 0)
            offers = await client.SearchAsync(search.Article, null, cancellationToken);

        return JapartsMapper.Map(offers, search.Article, search.Brand, aliases)
            .OrderBy(x => x.Price.Amount)
            .ToList();
    }
}
