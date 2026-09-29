using Microsoft.Extensions.Caching.Memory;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.MlAuto;

/// <summary>Один контур ML-Auto (BY или RU): общая логика, разные адрес, логин и валюта.</summary>
public sealed class MlAutoProvider : IQuotationProvider
{
    private static readonly TimeSpan ImportersTtl = TimeSpan.FromMinutes(30);

    private readonly MlAutoAccount _account;
    private readonly MlAutoOptions _options;
    private readonly MlAutoClient _client;
    private readonly ICurrencyConverter _currency;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly IProducerAliasService _aliases;

    public MlAutoProvider(MlAutoAccount account, MlAutoOptions options, MlAutoClient client, ICurrencyConverter currency,
        IMemoryCache cache, TimeProvider clock, IProducerAliasService aliases)
    {
        _aliases = aliases;
        _account = account;
        _options = options;
        _client = client;
        _currency = currency;
        _cache = cache;
        _clock = clock;
    }

    public string Key => _account.Key;
    public string Name => _account.Name;
    public string? LogoFile => _account.LogoUrl;
    public string? AccountLogin => _options.Login;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(search.Brand))
            throw new MlAutoApiException("ML-Auto ищет только по паре артикул + бренд: укажите бренд.");

        var conditionsTask = GetConditionsAsync(cancellationToken);
        // API принимает бренд только в своём написании («WYNNS», а не «WYNN'S», «KYB», а не «Kayaba»): пробуем исходное,
        // очищенное и известные алиасы из базы Studio2.
        var aliases = await _aliases.GetMapAsync(cancellationToken);
        var searches = aliases.QueryVariants(search.Brand).Select(b => _client.SearchAsync(search.Article, b, cancellationToken)).ToList();
        await Task.WhenAll(searches.Cast<Task>().Append(conditionsTask));
        var found = searches.SelectMany(t => t.Result).DistinctBy(x => string.Join("|", x.Pin, x.StorageCode, x.Price, x.Date)).ToList();

        var now = _clock.GetUtcNow();
        var result = new List<QuotationOffer>();
        // Контракт: только оригиналы (ANALOG = NO) запрошенного артикула.
        foreach (var offer in found.Where(x => string.Equals(x.Analog, "NO", StringComparison.OrdinalIgnoreCase)))
        {
            var price = MlAutoMapper.ParseDecimal(offer.Price);
            var rub = price is null ? null : await _currency.ToRubAsync(price.Value, _account.Currency, cancellationToken);
            if (MlAutoMapper.Map(offer, _account.Location, _account.Currency, conditionsTask.Result, now, rub) is { } mapped)
                result.Add(mapped);
        }

        return result.OrderBy(x => x.PriceRub?.Amount ?? x.Price.Amount).ToList();
    }

    /// <summary>ID склада → условия поставки. Справочник вспомогательный: при сбое условия просто не показываются.</summary>
    private async Task<IReadOnlyDictionary<string, string>> GetConditionsAsync(CancellationToken ct)
    {
        var key = $"mlauto:{_account.Key}:importers";
        if (_cache.TryGetValue(key, out IReadOnlyDictionary<string, string>? cached) && cached is not null) return cached;

        try
        {
            var map = (await _client.GetImportersAsync(ct))
                .Select(x => (Id: x.Id?.Trim(), Text: MlAutoMapper.CleanText(x.Description)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Text))
                .GroupBy(x => x.Id!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().Text!, StringComparer.OrdinalIgnoreCase);
            _cache.Set(key, (IReadOnlyDictionary<string, string>)map, ImportersTtl);
            return map;
        }
        catch (Exception ex) when (ex is MlAutoApiException or HttpRequestException)
        {
            return new Dictionary<string, string>();
        }
    }
}
