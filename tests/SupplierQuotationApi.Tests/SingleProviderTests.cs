using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Providers;

namespace SupplierQuotationApi.Tests;

/// <summary>Запрос по одному поставщику: не ждёт остальных, делит кэш с общей проценкой, ошибки приходят статусом.</summary>
public class SingleProviderTests
{
    private const string Key = "test-key";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private sealed class FakeProvider(string key, int delayMs = 0, bool enabled = true, bool fail = false) : IQuotationProvider
    {
        public int Calls;
        public string Key => key;
        public string Name => key;
        public string? LogoFile => null;
        public string? AccountLogin => "login-" + key;
        public bool IsEnabled => enabled;

        public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            await Task.Delay(delayMs, ct);
            if (fail) throw new InvalidOperationException("boom");
            return [new QuotationOffer { Article = search.Article, Brand = search.Brand, Price = new Money(10, "RUB") }];
        }
    }

    private static (HttpClient Client, WebApplicationFactory<Program> Factory) Create(params FakeProvider[] fakes)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("App:ApiKey", Key);
            b.UseSetting("Studio2Db:ConnectionString", "");
            b.ConfigureServices(s => { foreach (var fake in fakes) s.AddSingleton<IQuotationProvider>(fake); });
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", Key);
        return (client, factory);
    }

    [Fact]
    public async Task Single_DoesNotWaitForSlowProviders()
    {
        var fast = new FakeProvider("FakeFast");
        var slow = new FakeProvider("FakeSlow", delayMs: 3000);
        var (client, factory) = Create(fast, slow);
        using var _ = factory;

        var timer = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync("/api/quotations/providers/FakeFast", new { article = "K1223A", brand = "Filtron" });
        timer.Stop();

        var result = await response.Content.ReadFromJsonAsync<ProviderQuotation>(Web);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(QuotationStatus.Ok, result!.Status);
        Assert.Equal("login-FakeFast", result.AccountLogin);
        Assert.Single(result.Offers);
        Assert.True(timer.ElapsedMilliseconds < 1500, $"ответ занял {timer.ElapsedMilliseconds} мс: запрос ждал медленного поставщика");
        Assert.Equal(0, slow.Calls);          // медленный вообще не вызывался
    }

    [Fact]
    public async Task Single_KeyIsCaseInsensitive_AndReturnsCanonicalKey()
    {
        var (client, factory) = Create(new FakeProvider("FakeFast"));
        using var _ = factory;

        var result = await client.GetFromJsonAsync<ProviderQuotation>("/api/quotations/providers/fakefast?article=K1223A", Web);

        Assert.Equal("FakeFast", result!.ProviderKey);
    }

    [Fact]
    public async Task Single_GetAndPost_ReturnSameResult_AndShareCacheWithBatch()
    {
        var fake = new FakeProvider("FakeFast");
        var (client, factory) = Create(fake);
        using var _ = factory;

        var post = await (await client.PostAsJsonAsync("/api/quotations/providers/FakeFast", new { article = "K1223A", brand = "Filtron" }))
            .Content.ReadFromJsonAsync<ProviderQuotation>(Web);
        var get = await client.GetFromJsonAsync<ProviderQuotation>("/api/quotations/providers/FakeFast?article=K1223A&brand=Filtron", Web);
        var batch = await (await client.PostAsJsonAsync("/api/quotations", new { article = "K1223A", brand = "Filtron", providers = new[] { "FakeFast" } }))
            .Content.ReadFromJsonAsync<QuotationResponse>(Web);

        Assert.Equal(post!.Offers.Count, get!.Offers.Count);
        Assert.Equal(QuotationStatus.Ok, batch!.Providers.Single().Status);
        Assert.Equal(1, fake.Calls);          // GET и общий запрос взяли результат из кэша, поставщик вызван один раз
    }

    [Fact]
    public async Task Single_UnknownProvider_Returns404ProblemDetails()
    {
        var (client, factory) = Create();
        using var _ = factory;

        var response = await client.PostAsJsonAsync("/api/quotations/providers/Foo", new { article = "K1223A" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Foo", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/quotations/providers/Foo?article=K1223A")).StatusCode);
    }

    [Fact]
    public async Task Single_EmptyArticle_Returns400()
    {
        var (client, factory) = Create(new FakeProvider("FakeFast"));
        using var _ = factory;

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/quotations/providers/FakeFast", new { article = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/quotations/providers/FakeFast")).StatusCode);   // нет параметра article
    }

    [Fact]
    public async Task Single_DisabledAndFailingProviders_AreStatusesNotHttpErrors()
    {
        var (client, factory) = Create(new FakeProvider("FakeOff", enabled: false), new FakeProvider("FakeBoom", fail: true));
        using var _ = factory;

        var off = await client.GetFromJsonAsync<ProviderQuotation>("/api/quotations/providers/FakeOff?article=A", Web);
        var boom = await client.GetFromJsonAsync<ProviderQuotation>("/api/quotations/providers/FakeBoom?article=A", Web);

        Assert.Equal(QuotationStatus.Disabled, off!.Status);
        Assert.Equal(QuotationStatus.Error, boom!.Status);
        Assert.Equal("boom", boom.Error);
        Assert.Empty(boom.Offers);
    }

    [Fact]
    public async Task Single_RequiresApiKey()
    {
        var (_, factory) = Create(new FakeProvider("FakeFast"));
        using var _f = factory;
        using var anonymous = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/quotations/providers/FakeFast?article=A")).StatusCode);
    }

    [Fact]
    public async Task Single_PassesTrimmedArticleAndBrand_AndAnalogsFlag()
    {
        QuotationSearch? seen = null;
        var spy = new SpyProvider(s => seen = s);
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("App:ApiKey", Key);
            b.UseSetting("Studio2Db:ConnectionString", "");
            b.ConfigureServices(s => s.AddSingleton<IQuotationProvider>(spy));
        });
        using var _ = factory;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", Key);

        await client.PostAsJsonAsync("/api/quotations/providers/Spy", new { article = "  K1223A ", brand = " Filtron ", includeAnalogs = true });

        Assert.Equal(new QuotationSearch("K1223A", "Filtron", true), seen);
    }

    private sealed class SpyProvider(Action<QuotationSearch> onSearch) : IQuotationProvider
    {
        public string Key => "Spy";
        public string Name => "Spy";
        public string? LogoFile => null;
        public string? AccountLogin => null;
        public bool IsEnabled => true;

        public Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken ct)
        {
            onSearch(search);
            return Task.FromResult<IReadOnlyList<QuotationOffer>>([]);
        }
    }
}
