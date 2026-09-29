using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace SupplierQuotationApi.Core;

/// <summary>Курсы ЦБ РФ (cbr-xml-daily.ru), кэш 1 час. Один загрузчик на все запросы.</summary>
public sealed class CbrCurrencyConverter : ICurrencyConverter
{
    private const string CacheKey = "fx:cbr";
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public CbrCurrencyConverter(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<decimal?> ToRubAsync(decimal amount, string currency, CancellationToken cancellationToken)
    {
        if (string.Equals(currency, "RUB", StringComparison.OrdinalIgnoreCase)) return amount;

        var rates = await GetRatesAsync(cancellationToken);
        return rates.TryGetValue(currency.ToUpperInvariant(), out var rate) ? Math.Round(amount * rate, 2) : null;
    }

    private async Task<IReadOnlyDictionary<string, decimal>> GetRatesAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, decimal>? cached) && cached is not null)
            return cached;

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(CacheKey, out cached) && cached is not null) return cached;

            using var stream = await _http.GetStreamAsync("https://www.cbr-xml-daily.ru/daily_json.js", cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var rates = new Dictionary<string, decimal>();
            foreach (var valute in doc.RootElement.GetProperty("Valute").EnumerateObject())
            {
                var value = valute.Value.GetProperty("Value").GetDecimal();
                var nominal = valute.Value.GetProperty("Nominal").GetInt32();
                rates[valute.Name] = value / nominal;
            }

            _cache.Set(CacheKey, (IReadOnlyDictionary<string, decimal>)rates, TimeSpan.FromHours(1));
            return rates;
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
