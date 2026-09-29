using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core;
using SupplierQuotationApi.Infrastructure;
using SupplierQuotationApi.Providers;

namespace SupplierQuotationApi.Tests;

public class CoreTests
{
    private sealed class FakeProvider(string key, int delayMs, Func<int>? onCall = null, bool enabled = true, bool fail = false)
        : IQuotationProvider
    {
        public string Key => key;
        public string Name => key;
        public string? LogoFile => null;
        public string? AccountLogin => "login-" + key;
        public bool IsEnabled => enabled;

        public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken ct)
        {
            onCall?.Invoke();
            await Task.Delay(delayMs, ct);
            if (fail) throw new InvalidOperationException("boom");
            return [new QuotationOffer { Article = search.Article, Price = new Money(10, "RUB") }];
        }
    }

    private static QuotationService Service(int timeoutSeconds, int cacheSeconds, params IQuotationProvider[] providers) =>
        new(providers, new InflightResultCache(new MemoryCache(new MemoryCacheOptions())),
            Options.Create(new AppOptions { ProviderTimeoutSeconds = timeoutSeconds, ResultCacheSeconds = cacheSeconds }),
            NullLogger<QuotationService>.Instance);

    private static QuotationRequest Req(params string[] providers) =>
        new() { Article = "OC90", Providers = providers.Length == 0 ? null : providers };

    [Fact]
    public async Task SameConcurrentRequests_CallProviderOnce()
    {
        var calls = 0;
        var svc = Service(5, 60, new FakeProvider("A", 200, () => Interlocked.Increment(ref calls)));

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => svc.QuoteAsync(Req(), null, default)));
        await svc.QuoteAsync(Req(), null, default); // из кэша

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SlowProvider_GetsTimeout_OthersStillReturn()
    {
        var svc = Service(1, 0, new FakeProvider("Fast", 10), new FakeProvider("Slow", 5000));

        var response = await svc.QuoteAsync(Req(), null, default);

        Assert.Equal(QuotationStatus.Ok, response.Providers.Single(x => x.ProviderKey == "Fast").Status);
        Assert.Equal(QuotationStatus.Timeout, response.Providers.Single(x => x.ProviderKey == "Slow").Status);
        Assert.True(response.DurationMs < 3000);
    }

    [Fact]
    public async Task FailingProvider_ReturnsError_NotException()
    {
        var svc = Service(5, 0, new FakeProvider("Bad", 1, fail: true), new FakeProvider("Good", 1));

        var response = await svc.QuoteAsync(Req(), null, default);

        var bad = response.Providers.Single(x => x.ProviderKey == "Bad");
        Assert.Equal(QuotationStatus.Error, bad.Status);
        Assert.Equal("boom", bad.Error);
        Assert.Equal("login-Bad", bad.AccountLogin);
    }

    [Fact]
    public async Task Stream_YieldsInCompletionOrder()
    {
        var svc = Service(5, 0, new FakeProvider("Slow", 300), new FakeProvider("Fast", 10));

        var order = new List<string>();
        await foreach (var r in svc.QuoteStreamAsync(Req(), null, default)) order.Add(r.ProviderKey);

        Assert.Equal(["Fast", "Slow"], order);
    }

    [Fact]
    public async Task DisabledProvider_SkippedByDefault_ReportedWhenRequested()
    {
        var svc = Service(5, 0, new FakeProvider("Off", 1, enabled: false), new FakeProvider("On", 1));

        Assert.Single((await svc.QuoteAsync(Req(), null, default)).Providers);
        var explicitly = await svc.QuoteAsync(Req("Off"), null, default);
        Assert.Equal(QuotationStatus.Disabled, explicitly.Providers.Single().Status);
    }

    [Fact]
    public void DotEnv_LoadsQuotedValues_WithoutOverridingExisting()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, ".env"), "# c\nSQA_T1='a b'\nSQA_T2=\"it's\"\nSQA_T3=x=y\n");
        Environment.SetEnvironmentVariable("SQA_T3", "keep");
        var old = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(dir);
            DotEnv.Load();
        }
        finally { Directory.SetCurrentDirectory(old); }

        Assert.Equal("a b", Environment.GetEnvironmentVariable("SQA_T1"));
        Assert.Equal("it's", Environment.GetEnvironmentVariable("SQA_T2"));
        Assert.Equal("keep", Environment.GetEnvironmentVariable("SQA_T3"));
    }
}
