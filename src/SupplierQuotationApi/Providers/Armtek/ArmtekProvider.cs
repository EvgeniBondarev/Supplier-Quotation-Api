using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core;

namespace SupplierQuotationApi.Providers.Armtek;

/// <summary>Один аккаунт Armtek (RU или BY). Логика общая, различаются ключ, сбытовая организация и логин.</summary>
public sealed class ArmtekProvider : IQuotationProvider
{
    private const int MaxBrandCandidates = 6;
    private static readonly TimeSpan ReferenceTtl = TimeSpan.FromMinutes(30);

    private readonly ArmtekAccount _account;
    private readonly ArmtekOptions _options;
    private readonly ArmtekClient _client;
    private readonly ICurrencyConverter _currency;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;

    public ArmtekProvider(ArmtekAccount account, ArmtekOptions options, ArmtekClient client,
        ICurrencyConverter currency, IMemoryCache cache, TimeProvider clock)
    {
        _account = account;
        _options = options;
        _client = client;
        _currency = currency;
        _cache = cache;
        _clock = clock;
    }

    public string Key => _account.Key;
    public string Name => _account.Name;
    public string? LogoFile => _account.LogoFile;
    public string? AccountLogin => _options.User;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        var vkorg = _options.DefaultVkorg!;

        var kunnrTask = string.IsNullOrWhiteSpace(_options.BuyerKunnr)
            ? GetBuyerAsync(vkorg, cancellationToken)
            : Task.FromResult(_options.BuyerKunnr.Trim());
        var storesTask = GetStoreNamesAsync(vkorg, cancellationToken);
        var candidatesTask = ResolveCandidatesAsync(search, vkorg, cancellationToken);
        await Task.WhenAll(kunnrTask, storesTask, candidatesTask);

        var kunnr = kunnrTask.Result;
        var candidates = candidatesTask.Result;
        if (candidates.Count == 0) return [];

        var queryType = search.IncludeAnalogs ? "2" : "1";
        var responses = await Task.WhenAll(candidates.Select(c => _client.PostAsync<List<ArmtekSearchItem>>("ws_search/search",
            SearchForm(vkorg, kunnr, c, queryType), cancellationToken)));

        var now = _clock.GetUtcNow();
        var offers = new List<QuotationOffer>();
        foreach (var item in responses.SelectMany(r => r ?? []))
        {
            if (!search.IncludeAnalogs && item.Analog == "X") continue;

            var price = ArmtekMapper.ParseDecimal(item.Price);
            var currency = string.IsNullOrWhiteSpace(item.Currency) ? "RUB" : item.Currency!;
            var rub = price is null ? null : await _currency.ToRubAsync(price.Value, currency, cancellationToken);
            if (ArmtekMapper.Map(item, storesTask.Result, vkorg, kunnr, _options.DeliveryKunnr, now, rub) is { } offer) offers.Add(offer);
        }

        return offers.OrderBy(x => x.PriceRub?.Amount ?? x.Price.Amount).ToList();
    }

    private Dictionary<string, string> SearchForm(string vkorg, string kunnr, ArmtekAssortmentItem candidate, string queryType)
    {
        var form = new Dictionary<string, string>
        {
            ["VKORG"] = vkorg,
            ["KUNNR_RG"] = kunnr,
            ["PIN"] = candidate.Pin!,
            ["BRAND"] = candidate.Brand!,
            ["QUERY_TYPE"] = queryType
        };
        if (!string.IsNullOrWhiteSpace(_options.DeliveryKunnr))
        {
            form["KUNNR_ZA"] = _options.DeliveryKunnr.Trim();
            form["INCOTERMS"] = "0"; // доставка, не самовывоз
        }
        return form;
    }

    /// <summary>Пары PIN/BRAND для search. Armtek нормализует артикул сам (AP1086 → AP 108/6), поэтому
    /// каноническую пару берём из ассортимента: по бренду, а без бренда — все бренды этого кода.</summary>
    private async Task<IReadOnlyList<ArmtekAssortmentItem>> ResolveCandidatesAsync(QuotationSearch search, string vkorg,
        CancellationToken ct)
    {
        var key = $"armtek:{_account.Key}:assortment:{vkorg}:{Normalize(search.Article)}";
        if (!_cache.TryGetValue(key, out List<ArmtekAssortmentItem>? assortment) || assortment is null)
        {
            assortment = await _client.PostAsync<List<ArmtekAssortmentItem>>("ws_search/assortment_search",
                new Dictionary<string, string> { ["VKORG"] = vkorg, ["PIN"] = search.Article }, ct) ?? [];
            _cache.Set(key, assortment, ReferenceTtl);
        }

        var code = Normalize(search.Article);
        var byCode = assortment
            .Where(x => !string.IsNullOrWhiteSpace(x.Pin) && !string.IsNullOrWhiteSpace(x.Brand) && Normalize(x.Pin) == code)
            .DistinctBy(x => $"{x.Pin}|{Normalize(x.Brand)}")
            .ToList();
        if (string.IsNullOrWhiteSpace(search.Brand)) return byCode.Take(MaxBrandCandidates).ToList();

        var brand = Normalize(search.Brand);
        var exact = byCode.Where(x => Normalize(x.Brand) == brand).ToList();
        return exact.Count > 0
            ? exact
            : byCode.Where(x => Normalize(x.Brand) is { Length: > 0 } b && (b.Contains(brand) || brand.Contains(b))).ToList();
    }

    private async Task<string> GetBuyerAsync(string vkorg, CancellationToken ct)
    {
        var key = $"armtek:{_account.Key}:buyer:{vkorg}";
        if (_cache.TryGetValue(key, out string? cached) && cached is not null) return cached;

        var info = await _client.PostAsync<ArmtekUserInfo>("ws_user/getUserInfo",
            new Dictionary<string, string> { ["VKORG"] = vkorg, ["STRUCTURE"] = "1" }, ct);
        var buyers = info?.Structure?.Buyers ?? [];
        var kunnr = (buyers.FirstOrDefault(x => x.Default == "1" && !string.IsNullOrWhiteSpace(x.Kunnr))
                     ?? buyers.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Kunnr)))?.Kunnr;
        if (string.IsNullOrWhiteSpace(kunnr))
            throw new ArmtekApiException("Armtek getUserInfo не вернул ни одного покупателя (RG_TAB).");

        _cache.Set(key, kunnr, ReferenceTtl);
        return kunnr;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetStoreNamesAsync(string vkorg, CancellationToken ct)
    {
        var key = $"armtek:{_account.Key}:stores:{vkorg}";
        if (_cache.TryGetValue(key, out IReadOnlyDictionary<string, string>? cached) && cached is not null) return cached;

        try
        {
            var stores = (await _client.PostAsync<List<ArmtekStoreItem>>("ws_user/getStoreList",
                    new Dictionary<string, string> { ["VKORG"] = vkorg }, ct) ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.Keyzak) && !string.IsNullOrWhiteSpace(x.SklName))
                .GroupBy(x => x.Keyzak!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().SklName!, StringComparer.OrdinalIgnoreCase);
            _cache.Set(key, (IReadOnlyDictionary<string, string>)stores, ReferenceTtl);
            return stores;
        }
        catch (Exception ex) when (ex is ArmtekApiException or HttpRequestException)
        {
            // Справочник — подпись склада: без него склад показывается кодом KEYZAK.
            return new Dictionary<string, string>();
        }
    }

    private static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
