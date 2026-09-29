using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.ForumAuto;

/// <summary>Один аккаунт Forum-Auto. Каталог (gid) общий, но склады, цены и сроки у каждого аккаунта свои.</summary>
public sealed class ForumAutoProvider : IQuotationProvider
{
    private readonly ForumAutoAccount _account;
    private readonly ForumAutoOptions _options;
    private readonly ForumAutoClient _client;
    private readonly IProducerAliasService _aliases;

    public ForumAutoProvider(ForumAutoAccount account, ForumAutoOptions options, ForumAutoClient client,
        IProducerAliasService aliases)
    {
        _aliases = aliases;
        _account = account;
        _options = options;
        _client = client;
    }

    public string Key => _account.Key;
    public string Name => _account.Name;
    public string? LogoFile => "forum-auto.svg";
    public string? AccountLogin => _options.Login;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Бренд не передаём в br: API требует точное своё написание. Берём все бренды кода и фильтруем локально.
        var rows = await _client.ListGoodsAsync(search.Article, search.IncludeAnalogs, cancellationToken);

        var aliases = await _aliases.GetMapAsync(cancellationToken);
        return ForumAutoMapper.Map(rows, search.Article, search.Brand, search.IncludeAnalogs, aliases)
            .OrderBy(x => x.Price.Amount)
            .ToList();
    }
}
