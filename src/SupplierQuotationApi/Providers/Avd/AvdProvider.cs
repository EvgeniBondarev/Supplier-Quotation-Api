using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Avd;

public sealed class AvdProvider : IQuotationProvider
{
    private const int MaxCatalogs = 6;
    private readonly IServiceProvider _services;
    private readonly AvdOptions _options;
    private readonly IProducerAliasService _aliases;

    public AvdProvider(IServiceProvider services, IOptions<AvdOptions> options, IProducerAliasService aliases)
    {
        _aliases = aliases;
        _services = services;
        _options = options.Value;
    }

    public string Key => "Avd";
    public string Name => "АВД";
    public string? LogoFile => "avd.svg";
    public string? AccountLogin => _options.Login;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Клиент транзитный: свежий из фабрики, чтобы работала ротация обработчиков.
        var client = _services.GetRequiredService<AvdClient>();

        var aliases = await _aliases.GetMapAsync(cancellationToken);
        var catalogs = SelectCatalogs(await client.GetCatalogsAsync(search.Article, cancellationToken), search.Brand, aliases);
        if (catalogs.Count == 0) return [];

        var responses = await Task.WhenAll(catalogs.Select(c => client.GetOriginalPriceAsync(search.Article, c, cancellationToken)));

        // Только оригиналы запрошенного номера, кроссы АВД отдаёт отдельным методом и сюда не попадают.
        var requested = AvdMapper.Normalize(search.Article);
        var offers = responses.SelectMany(x => x)
            .Where(x => AvdMapper.Normalize(x.ItemNumber) == requested && x.IsOriginal != false);

        return AvdMapper.Map(offers).OrderBy(x => x.Price.Amount).ToList();
    }

    /// <summary>Каталог = бренд. С брендом берём совпавший (точно, иначе по вхождению), без бренда — все, но не больше лимита.
    /// Если бренд задан и совпадений нет — пусто: подставлять первый каталог значит вернуть чужой бренд.</summary>
    public static List<string> SelectCatalogs(IReadOnlyList<string> catalogs, string? brand, ProducerAliasMap? aliases = null)
    {
        if (string.IsNullOrWhiteSpace(brand)) return catalogs.Take(MaxCatalogs).ToList();

        aliases ??= ProducerAliasMap.Empty;
        var exact = catalogs.Where(c => aliases.AreSame(c, brand)).ToList();
        return exact.Count > 0 ? exact : catalogs.Where(c => aliases.Matches(c, brand)).ToList();
    }
}
