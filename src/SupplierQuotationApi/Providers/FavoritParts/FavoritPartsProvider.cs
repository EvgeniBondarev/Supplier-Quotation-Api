using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.FavoritParts;

/// <summary>Один аккаунт FavoritParts (Союз, Истра, Ростов): общая логика, разные ключи.</summary>
public sealed class FavoritPartsProvider : IQuotationProvider
{
    private readonly FavoritPartsAccount _account;
    private readonly FavoritPartsOptions _options;
    private readonly FavoritPartsClient _client;
    private readonly TimeProvider _clock;

    public FavoritPartsProvider(FavoritPartsAccount account, FavoritPartsOptions options, FavoritPartsClient client,
        TimeProvider clock)
    {
        _account = account;
        _options = options;
        _client = client;
        _clock = clock;
    }

    public string Key => _account.Key;
    public string Name => _account.Name;
    public string? LogoFile => _account.LogoFile;
    /// <summary>У FavoritParts нет логина: аккаунт идентифицирует ключ покупателя. Отдаём его начало, не весь секрет.</summary>
    public string? AccountLogin => string.IsNullOrWhiteSpace(_options.ClientKey) ? null : $"{_options.ClientKey[..Math.Min(8, _options.ClientKey.Length)]}…";
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Аналоги сервис отдаёт только вместе с брендом (analogues=on + brand).
        var withAnalogues = search.IncludeAnalogs && !string.IsNullOrWhiteSpace(search.Brand);
        var goods = await _client.SearchAsync(search.Article, search.Brand, withAnalogues, cancellationToken);

        return FavoritPartsMapper.Map(goods, search.Article, search.Brand, withAnalogues, _clock.GetUtcNow())
            .OrderBy(x => x.Price.Amount)
            .ToList();
    }
}
