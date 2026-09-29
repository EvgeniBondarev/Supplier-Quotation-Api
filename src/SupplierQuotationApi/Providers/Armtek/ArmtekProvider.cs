using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Armtek;

/// <summary>Один аккаунт Armtek (RU или BY). Логика общая, различаются ключ, сбытовая организация и логин.</summary>
public sealed class ArmtekProvider : IQuotationProvider
{
    private const int MaxBrandCandidates = 6;
    private static readonly TimeSpan ReferenceTtl = TimeSpan.FromMinutes(30);
    /// <summary>Справочник складов Armtek весит до 8 МБ: на медленном канале он грузится десятки секунд. Проценку он не блокирует —
    /// после поиска ждём его не дольше этого времени, иначе склад подписывается кодом KEYZAK, а загрузка доводится в фоне.</summary>
    public static readonly TimeSpan DefaultStoreNamesGrace = TimeSpan.FromMilliseconds(2500);

    private readonly ArmtekAccount _account;
    private readonly ArmtekOptions _options;
    private readonly ArmtekClient _client;
    private readonly ICurrencyConverter _currency;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _clock;
    private readonly IProducerAliasService _aliases;
    private readonly TimeSpan _storeNamesGrace;
    private readonly object _storesLock = new();
    private Task<IReadOnlyDictionary<string, string>>? _storesLoading;

    public ArmtekProvider(ArmtekAccount account, ArmtekOptions options, ArmtekClient client,
        ICurrencyConverter currency, IMemoryCache cache, TimeProvider clock, IProducerAliasService aliases,
        TimeSpan? storeNamesGrace = null)
    {
        _storeNamesGrace = storeNamesGrace ?? DefaultStoreNamesGrace;
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
    public string? LogoFile => _account.LogoFile;
    public string? AccountLogin => _options.User;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        var vkorg = _options.DefaultVkorg!;

        var kunnrTask = string.IsNullOrWhiteSpace(_options.BuyerKunnr)
            ? GetBuyerAsync(vkorg, cancellationToken)
            : Task.FromResult(_options.BuyerKunnr.Trim());
        var storesTask = StartStoreNamesLoad(vkorg);     // не ждём: справочник большой, грузится параллельно с поиском
        var candidatesTask = ResolveCandidatesAsync(search, vkorg, cancellationToken);
        await Task.WhenAll(kunnrTask, candidatesTask);

        var kunnr = kunnrTask.Result;
        var candidates = candidatesTask.Result;
        if (candidates.Count == 0) return [];

        var queryType = search.IncludeAnalogs ? "2" : "1";
        var responses = await Task.WhenAll(candidates.Select(c => _client.PostAsync<List<ArmtekSearchItem>>("ws_search/search",
            SearchForm(vkorg, kunnr, c, queryType), cancellationToken)));

        var storeNames = await WaitBrieflyAsync(storesTask, cancellationToken);

        var now = _clock.GetUtcNow();
        var offers = new List<QuotationOffer>();
        foreach (var item in responses.SelectMany(r => r ?? []))
        {
            if (!search.IncludeAnalogs && item.Analog == "X") continue;

            var price = ArmtekMapper.ParseDecimal(item.Price);
            var currency = string.IsNullOrWhiteSpace(item.Currency) ? "RUB" : item.Currency!;
            var rub = price is null ? null : await _currency.ToRubAsync(price.Value, currency, cancellationToken);
            if (ArmtekMapper.Map(item, storeNames, vkorg, kunnr, _options.DeliveryKunnr, now, rub) is { } offer) offers.Add(offer);
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

        var aliases = await _aliases.GetMapAsync(ct);
        var exact = byCode.Where(x => aliases.AreSame(x.Brand, search.Brand)).ToList();
        return exact.Count > 0 ? exact : byCode.Where(x => aliases.Matches(x.Brand, search.Brand)).ToList();
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

    /// <summary>Запускает (или подхватывает уже идущую) загрузку названий складов. Единая на аккаунт: параллельные проценки
    /// не качают 8 МБ повторно. Загрузка не привязана к запросу пользователя и доводится до конца в фоне, результат кэшируется.</summary>
    private Task<IReadOnlyDictionary<string, string>> StartStoreNamesLoad(string vkorg)
    {
        var key = $"armtek:{_account.Key}:stores:{vkorg}";
        if (_cache.TryGetValue(key, out IReadOnlyDictionary<string, string>? cached) && cached is not null)
            return Task.FromResult(cached);

        lock (_storesLock)
            // Task.Run: загрузка не должна выполняться внутри lock (её finally тоже берёт lock и мог бы затереть свежее значение).
            return _storesLoading ??= Task.Run(() => LoadStoreNamesAsync(vkorg, key));
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadStoreNamesAsync(string vkorg, string cacheKey)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var stores = (await _client.PostAsync<List<ArmtekStoreItem>>("ws_user/getStoreList",
                    new Dictionary<string, string> { ["VKORG"] = vkorg }, cts.Token) ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x.Keyzak) && !string.IsNullOrWhiteSpace(x.SklName))
                .GroupBy(x => x.Keyzak!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().SklName!, StringComparer.OrdinalIgnoreCase);
            _cache.Set(cacheKey, (IReadOnlyDictionary<string, string>)stores, ReferenceTtl);
            return stores;
        }
        catch (Exception ex) when (ex is ArmtekApiException or HttpRequestException or OperationCanceledException)
        {
            // Справочник — подпись склада: без него склад показывается кодом KEYZAK.
            return new Dictionary<string, string>();
        }
        finally
        {
            lock (_storesLock) _storesLoading = null;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> WaitBrieflyAsync(Task<IReadOnlyDictionary<string, string>> loading,
        CancellationToken ct)
    {
        if (loading.IsCompleted) return await loading;

        var winner = await Task.WhenAny(loading, Task.Delay(_storeNamesGrace, _clock, ct));
        return winner == loading ? await loading : new Dictionary<string, string>();
    }

    private static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);
}
