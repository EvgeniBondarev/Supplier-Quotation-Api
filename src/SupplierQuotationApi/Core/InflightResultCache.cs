using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Core;

/// <summary>Кэш результатов проценки с single-flight: одинаковые одновременные запросы делают один вызов к поставщику.</summary>
public sealed class InflightResultCache
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<QuotationOffer>>>> _inflight = new();

    public InflightResultCache(IMemoryCache cache) => _cache = cache;

    /// <param name="fetch">Вызов поставщика. Получает собственный токен с таймаутом, а не токен вызывающего:
    /// отмена одного клиента не должна рвать общий запрос.</param>
    public Task<IReadOnlyList<QuotationOffer>> GetOrFetchAsync(string key, TimeSpan ttl, TimeSpan timeout,
        Func<CancellationToken, Task<IReadOnlyList<QuotationOffer>>> fetch)
    {
        if (ttl > TimeSpan.Zero && _cache.TryGetValue(key, out IReadOnlyList<QuotationOffer>? cached) && cached is not null)
            return Task.FromResult(cached);

        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<IReadOnlyList<QuotationOffer>>>(
            () => FetchAsync(key, ttl, timeout, fetch)));
        return lazy.Value;
    }

    private async Task<IReadOnlyList<QuotationOffer>> FetchAsync(string key, TimeSpan ttl, TimeSpan timeout,
        Func<CancellationToken, Task<IReadOnlyList<QuotationOffer>>> fetch)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            IReadOnlyList<QuotationOffer> offers;
            try
            {
                offers = await fetch(cts.Token);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException($"Поставщик не ответил за {timeout.TotalSeconds:0} с.");
            }

            if (ttl > TimeSpan.Zero) _cache.Set(key, offers, ttl);
            return offers;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }
}
