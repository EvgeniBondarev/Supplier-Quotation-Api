using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Core;
using SupplierQuotationApi.Core.ProducerAliases;
using SupplierQuotationApi.Providers;
using SupplierQuotationApi.Providers.Armtek;

namespace SupplierQuotationApi.Tests;

/// <summary>Справочник складов Армтека весит до 8 МБ и на медленном канале грузится десятки секунд: он не должен блокировать проценку.</summary>
public class ArmtekProviderTests
{
    private const string Assortment = """{"STATUS":200,"MESSAGES":[],"RESP":[{"PIN":"SF-2364","BRAND":"ZEKKERT","NAME":"Пружина"}]}""";
    private const string Search = """{"STATUS":200,"MESSAGES":[],"RESP":[{"ARTID":"1","KEYZAK":"0000170915","PIN":"SF-2364","BRAND":"ZEKKERT","NAME":"Пружина","PRICE":"1385.75","WAERS":"RUB","RVALUE":"2","DLVDT":"20261002113000"}]}""";
    private const string Stores = """{"STATUS":200,"MESSAGES":[],"RESP":[{"KEYZAK":"0000170915","SKLNAME":"ЦЗ Москва"}]}""";

    private sealed class Handler(int storeDelayMs) : HttpMessageHandler
    {
        public int StoreCalls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("getStoreList"))
            {
                Interlocked.Increment(ref StoreCalls);
                await Task.Delay(storeDelayMs, CancellationToken.None);   // как медленное скачивание: запрос пользователя его не отменяет
                return Json(Stores);
            }
            return Json(path.EndsWith("assortment_search") ? Assortment : Search);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    private sealed class FixedAliases : IProducerAliasService
    {
        public Task<ProducerAliasMap> GetMapAsync(CancellationToken ct) => Task.FromResult(ProducerAliasMap.Empty);
    }

    private sealed class Rub : ICurrencyConverter
    {
        public Task<decimal?> ToRubAsync(decimal amount, string currency, CancellationToken ct) => Task.FromResult<decimal?>(amount);
    }

    private static (ArmtekProvider Provider, Handler Handler) Create(int storeDelayMs, int graceMs)
    {
        var handler = new Handler(storeDelayMs);
        var options = new ArmtekOptions { User = "u", Password = "p", DefaultVkorg = "4000", BuyerKunnr = "10000001" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://ws.armtek.ru/") };
        var account = new ArmtekAccount("Armtek", "Армтек", "armtek.svg", "Suppliers:Armtek", "Armtek");
        var provider = new ArmtekProvider(account, options, new ArmtekClient(http, options, TimeProvider.System), new Rub(),
            new MemoryCache(new MemoryCacheOptions()), TimeProvider.System, new FixedAliases(), TimeSpan.FromMilliseconds(graceMs));
        return (provider, handler);
    }

    [Fact]
    public async Task SlowStoreList_DoesNotBlockSearch_WarehouseFallsBackToCode()
    {
        var (provider, _) = Create(storeDelayMs: 3000, graceMs: 150);

        var timer = Stopwatch.StartNew();
        var offers = await provider.SearchAsync(new QuotationSearch("SF2364", "Zekkert", false), default);
        timer.Stop();

        Assert.True(timer.ElapsedMilliseconds < 1500, $"поиск занял {timer.ElapsedMilliseconds} мс: дождался справочника складов");
        Assert.Equal("0000170915", Assert.Single(offers).Warehouse);      // название ещё не загружено — склад подписан кодом
    }

    [Fact]
    public async Task StoreList_FinishesInBackground_AndNamesAppearOnNextSearch()
    {
        var (provider, handler) = Create(storeDelayMs: 400, graceMs: 50);

        var first = await provider.SearchAsync(new QuotationSearch("SF2364", "Zekkert", false), default);
        await Task.Delay(900);                                            // фоновая загрузка завершилась и попала в кэш
        var second = await provider.SearchAsync(new QuotationSearch("SF2364", "Zekkert", false), default);

        Assert.Equal("0000170915", first[0].Warehouse);
        Assert.Equal("ЦЗ Москва (0000170915)", second[0].Warehouse);
        Assert.Equal(1, handler.StoreCalls);                              // 8 МБ скачаны один раз
    }

    [Fact]
    public async Task ConcurrentSearches_ShareOneStoreListDownload()
    {
        var (provider, handler) = Create(storeDelayMs: 500, graceMs: 20);

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => provider.SearchAsync(new QuotationSearch("SF2364", "Zekkert", false), default)));

        Assert.Equal(1, handler.StoreCalls);
    }

    [Fact]
    public async Task FastStoreList_IsUsedRightAway()
    {
        var (provider, _) = Create(storeDelayMs: 20, graceMs: 1500);

        var offers = await provider.SearchAsync(new QuotationSearch("SF2364", "Zekkert", false), default);

        Assert.Equal("ЦЗ Москва (0000170915)", Assert.Single(offers).Warehouse);
    }
}
