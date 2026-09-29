using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.ForumAuto;

/// <summary>Один аккаунт Forum-Auto. Каталог (gid) общий, но склады, цены и сроки у каждого аккаунта свои.</summary>
public sealed class ForumAutoProvider : IQuotationProvider
{
    private readonly ForumAutoAccount _account;
    private readonly ForumAutoOptions _options;
    private readonly ForumAutoClient _client;

    public ForumAutoProvider(ForumAutoAccount account, ForumAutoOptions options, ForumAutoClient client)
    {
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

        return ForumAutoMapper.Map(rows, search.Article, search.Brand, search.IncludeAnalogs)
            .OrderBy(x => x.Price.Amount)
            .ToList();
    }
}
