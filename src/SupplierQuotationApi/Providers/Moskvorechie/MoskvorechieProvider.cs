using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Moskvorechie;

public sealed class MoskvorechieProvider : IQuotationProvider
{
    private readonly IServiceProvider _services;
    private readonly MoskvorechieOptions _options;
    private readonly IProducerAliasService _aliases;

    public MoskvorechieProvider(IServiceProvider services, IOptions<MoskvorechieOptions> options, IProducerAliasService aliases)
    {
        _aliases = aliases;
        _services = services;
        _options = options.Value;
    }

    public string Key => "MoskvorechieIstra";
    public string Name => "Москворечье (Истра)";
    public string? LogoFile => "moskvorechie.gif";
    public string? AccountLogin => _options.Login;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // 1. Портал: склад, наличие, срок и подтверждённый бренд.
        if (_options.PortalConfigured)
        {
            var portal = await _services.GetRequiredService<MoskvorechiePortalClient>()
                .SearchAsync(search.Article, search.Brand, cancellationToken, await _aliases.GetMapAsync(cancellationToken));
            if (portal.Count > 0)
                return portal.Select(MoskvorechieMapper.MapPortal).OrderBy(x => x.Price.Amount).ToList();
        }

        // 2. API price_by_nr_firm игнорирует фирму и способен вернуть другой бренд при том же номере.
        // Для проценки с брендом это хуже пустого результата: позиции без подтверждённого производителя не показываем.
        if (!string.IsNullOrWhiteSpace(search.Brand) || !_options.ApiConfigured) return [];

        var rows = await _services.GetRequiredService<MoskvorechieApiClient>().SearchPricesAsync(search.Article, cancellationToken);
        return rows.Select(r => MoskvorechieMapper.MapApi(r, search.Article, search.Brand))
            .OfType<QuotationOffer>().OrderBy(x => x.Price.Amount).ToList();
    }
}
