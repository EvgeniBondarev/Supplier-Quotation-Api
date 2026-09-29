using System.Net;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Providers.Motex;

namespace SupplierQuotationApi.Tests;

public class MotexTests
{
    // 2026-10-01 08:30 UTC = 11:30 в Минске.
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 30, 0, TimeSpan.Zero);

    private static MotexArticle Article(string? price = "12.5", string? quantity = "20>", string? multiple = "2",
        string? deliveryDate = "20261002113000", int? returnPeriod = 14, int? resolutionTag = null) => new()
    {
        Code = "154128601", Brand = "Victor Reinz", Description = "Прокладка", Price = price, Quantity = quantity,
        Multiple = multiple, StoreCode = "MSK1", StoreInfo = "Минск, склад 1", DeliveryDate = deliveryDate,
        ReturnPeriod = returnPeriod, ResolutionTag = resolutionTag, RemainingMarkup = "3.5"
    };

    [Fact]
    public void Map_FillsUnifiedOffer()
    {
        var offer = MotexMapper.Map(Article(), "BYN", Now, 348.75m)!;

        Assert.Equal("154128601|MSK1|12.5", offer.OfferId);
        Assert.Equal("Минск, склад 1", offer.Warehouse);
        Assert.Equal(12.5m, offer.Price.Amount);
        Assert.Equal("BYN", offer.Price.Currency);
        Assert.Equal(348.75m, offer.PriceRub!.Amount);
        Assert.Equal(20, offer.Stock);
        Assert.Equal(">20", offer.StockText);       // «20>» = «от 20 шт.»
        Assert.Equal(2, offer.MinOrderQuantity);
        Assert.Equal(1, offer.DeliveryDaysMin);      // по Минску ровно +1 день
        Assert.Equal("Возврат: 14 дн.", offer.PriceNote);
    }

    [Fact]
    public void Map_Decree713_AddsMarkupToProviderData()
    {
        var offer = MotexMapper.Map(Article(resolutionTag: 1), "BYN", Now, null)!;

        Assert.Equal("true", offer.ProviderData!["decree713"]);
        Assert.Equal("3.5", offer.ProviderData["remainingMarkup"]);
        Assert.Null(MotexMapper.Map(Article(), "BYN", Now, null)!.ProviderData!["decree713"]);
    }

    [Theory]
    [InlineData("20>", 20)]
    [InlineData("1,5", 1.5)]
    [InlineData("7", 7)]
    public void ParseNumber_TakesNumericPart(string raw, double expected) =>
        Assert.Equal((decimal)expected, MotexMapper.ParseNumber(raw));

    [Fact]
    public void Map_WithoutPrice_ReturnsNull_AndBadDateGivesNullDays()
    {
        Assert.Null(MotexMapper.Map(Article(price: null), "BYN", Now, null));
        Assert.Null(MotexMapper.DeliveryDays("bad", Now));
        Assert.Equal(0, MotexMapper.DeliveryDays("20250101000000", Now));
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<string> Calls = [];
        public readonly List<string?> Auth = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            Auth.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private const string LoginOk = """{"status":200,"messages":[],"response":{"token":"tok-1","expireIn":3600}}""";

    private static (MotexClient, ScriptedHandler) Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new ScriptedHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.motexc.by/") };
        var options = Options.Create(new MotexOptions { Login = "l", Password = "p" });
        return (new MotexClient(http, new MotexTokenCache(options, TimeProvider.System)), handler);
    }

    [Fact]
    public async Task Search_LogsInOnce_ReusesToken_AndParsesArticles()
    {
        var (client, handler) = Client(r => r.RequestUri!.AbsolutePath.EndsWith("Auth/login")
            ? Ok(LoginOk)
            : Ok("""{"status":200,"messages":[],"response":{"articles":[{"code":"154128601","brand":"VR","price":12.5,"quantity":"20>","storeCode":5}]}}"""));

        var first = await client.SearchAsync("154128601", "Victor Reinz", "10001", default);
        await client.SearchAsync("154128601", null, null, default);

        var article = Assert.Single(first);
        Assert.Equal("12.5", article.Price);        // число → строка
        Assert.Equal("5", article.StoreCode);
        Assert.Equal(1, handler.Calls.Count(c => c.Contains("Auth/login")));   // токен переиспользован
        Assert.Contains("deliveryAddressCode=10001", handler.Calls[1]);
        Assert.Contains("withAnalogs=0", handler.Calls[1]);
        Assert.Equal("Bearer tok-1", handler.Auth[1]);
    }

    [Fact]
    public async Task Search_404InEnvelope_IsEmptyNotError()
    {
        var (client, _) = Client(r => r.RequestUri!.AbsolutePath.EndsWith("Auth/login")
            ? Ok(LoginOk)
            : Ok("""{"status":404,"messages":{"errorText":"Не найдено"},"response":[]}"""));

        Assert.Empty(await client.SearchAsync("X", null, null, default));
    }

    [Fact]
    public async Task Login_IpForbidden_ThrowsWithReadableText()
    {
        var (client, _) = Client(_ => Ok("""{"status":403,"messages":{"errorText":"Нет доступа. Проверьте IP адрес.(текущий IP: 1.2.3.4)"},"response":[]}"""));

        var ex = await Assert.ThrowsAsync<MotexApiException>(() => client.SearchAsync("X", null, null, default));
        Assert.Equal(403, ex.StatusCode);
        Assert.Contains("Проверьте IP", ex.Message);
    }

    [Fact]
    public async Task Search_401_RefreshesTokenOnce()
    {
        var logins = 0;
        var searches = 0;
        var (client, handler) = Client(r =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("Auth/login"))
            {
                logins++;
                return Ok("{\"status\":200,\"messages\":[],\"response\":{\"token\":\"tok-" + logins + "\",\"expireIn\":3600}}");
            }
            return ++searches == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") }
                : Ok("""{"status":200,"messages":[],"response":{"articles":[]}}""");
        });

        await client.SearchAsync("X", null, null, default);

        Assert.Equal(2, logins);
        Assert.Equal("Bearer tok-2", handler.Auth.Last());
    }
}
