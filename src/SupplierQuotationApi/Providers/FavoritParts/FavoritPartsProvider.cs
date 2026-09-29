using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.FavoritParts;

/// <summary>Один аккаунт FavoritParts (Союз, Истра, Ростов): общая логика, разные ключи.</summary>
public sealed class FavoritPartsProvider : IQuotationProvider
{
    private readonly FavoritPartsAccount _account;
    private readonly FavoritPartsOptions _options;
    private readonly FavoritPartsClient _client;
    private readonly TimeProvider _clock;
    private readonly IProducerAliasService _aliases;

    public FavoritPartsProvider(FavoritPartsAccount account, FavoritPartsOptions options, FavoritPartsClient client,
        TimeProvider clock, IProducerAliasService aliases)
    {
        _aliases = aliases;
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
        // Бренд в API передаём только с аналогами. Без него сервис отдаёт все бренды артикула, и бренд запроса сверяется
        // здесь, с алиасами: так «Kayaba» находит предложения, которые FavoritParts подписывает «KYB».
        var goods = await _client.SearchAsync(search.Article, withAnalogues ? search.Brand : null, withAnalogues, cancellationToken);

        var aliases = await _aliases.GetMapAsync(cancellationToken);
        return FavoritPartsMapper.Map(goods, search.Article, search.Brand, withAnalogues, _clock.GetUtcNow(), aliases)
            .OrderBy(x => x.Price.Amount)
            .ToList();
    }
}
