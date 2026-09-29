using System.Net;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Providers.Moskvorechie;

namespace SupplierQuotationApi.Tests;

public class MoskvorechieTests
{
    private const string SearchHtml = """
        <table><tr><td><b>Metalcaucho</b> <a href="good_info.lmc?gid=4444&amp;cat_id=2">04114</a></td></tr>
        <tr><td><b>Other</b> <a href="good_info.lmc?gid=5555&amp;cat_id=2">04114</a></td></tr>
        <tr><td><b>Metalcaucho</b> <a href="good_info.lmc?gid=6666">04115</a></td></tr></table>
        """;

    private const string PriceHtml = """
        <table>
        <tr><td></td><td>Metalcaucho</td><td>04114</td><td>Прокладка</td><td>2</td><td></td><td>Запад</td><td>на складе</td><td>1</td><td>327 руб.</td><td><a href="x?to_basket_gid=1002368873&amp;q=1">в корзину</a></td></tr>
        <tr><td></td><td>Metalcaucho</td><td>04114</td><td>Прокладка</td><td>&gt;20</td><td></td><td>Центр</td><td>1 день</td><td>2</td><td>1 234,50 руб.</td><td><a href="x?to_basket_gid=1004534458">в корзину</a></td></tr>
        <tr><td></td><td>Metalcaucho</td><td>99999</td><td>Чужой артикул</td><td>1</td><td></td><td>Юг</td><td>2 дня</td><td>1</td><td>10 руб.</td><td><a href="x?to_basket_gid=7">в корзину</a></td></tr>
        </table>
        """;

    [Fact]
    public void FindGid_MatchesArticleAndBrandInHeader()
    {
        Assert.Equal(4444, MoskvorechiePortalClient.FindGid(SearchHtml, "04114", "Metalcaucho"));
        Assert.Equal(5555, MoskvorechiePortalClient.FindGid(SearchHtml, "04114", "Other"));
        Assert.Equal(4444, MoskvorechiePortalClient.FindGid(SearchHtml, "04-114", null));   // без бренда — первый
        Assert.Null(MoskvorechiePortalClient.FindGid(SearchHtml, "04114", "Zekkert"));
        Assert.Null(MoskvorechiePortalClient.FindGid(SearchHtml, "77777", null));
    }

    [Fact]
    public void ParseOffers_ReadsRows_SkipsOtherArticles()
    {
        var offers = MoskvorechiePortalClient.ParseOffers(PriceHtml, "04114", "Metalcaucho");

        Assert.Equal(2, offers.Count);
        Assert.Equal("1002368873", offers[0].OfferId);
        Assert.Equal("Запад", offers[0].Warehouse);
        Assert.Equal(327m, offers[0].Price);
        Assert.Equal(1234.5m, offers[1].Price);      // «1 234,50 руб.»: запятая — десятичная, пробел — тысячи
    }

    [Theory]
    [InlineData("327 руб.", 327)]
    [InlineData("3026.88", 3026.88)]
    [InlineData("1 234,50 руб.", 1234.5)]
    [InlineData("1,234", 1234)]           // запятая с тремя цифрами — разделитель тысяч
    [InlineData("1,234.50", 1234.5)]
    public void ParsePrice_HandlesPortalFormats(string raw, double expected) =>
        Assert.Equal((decimal)expected, MoskvorechiePortalClient.ParsePrice(raw));

    [Fact]
    public void ParsePrice_Garbage_IsNull() => Assert.Null(MoskvorechiePortalClient.ParsePrice("по запросу"));

    [Theory]
    [InlineData("""1{"body":"<b>x</b>"}""", "<b>x</b>")]
    [InlineData("""{"body":"y"}""", "y")]
    public void ExtractBody_HandlesLeadingOne(string raw, string expected) =>
        Assert.Equal(expected, MoskvorechiePortalClient.ExtractBody(raw));

    [Fact]
    public void ExtractBody_InvalidJson_Throws() =>
        Assert.Throws<MoskvorechieApiException>(() => MoskvorechiePortalClient.ExtractBody("<html>login</html>"));

    [Theory]
    [InlineData("3 дня", 3)]
    [InlineData("1 день", 1)]
    [InlineData("на складе", 0)]
    [InlineData("не известно", null)]
    [InlineData(null, null)]
    public void ParseDays_HandlesWordsAndNumbers(string? raw, int? expected) =>
        Assert.Equal(expected, MoskvorechieMapper.ParseDays(raw));

    [Fact]
    public void MapPortal_FillsUnifiedOffer()
    {
        var offer = MoskvorechieMapper.MapPortal(new MoskvorechiePortalOffer("100", "Metalcaucho", "04114", "Прокладка", ">20", "Центр", "1 день", 2, 327m));

        Assert.Equal("100", offer.OfferId);
        Assert.Equal("Центр", offer.Warehouse);
        Assert.Equal(20, offer.Stock);
        Assert.Equal(">20", offer.StockText);
        Assert.Equal(2, offer.MinOrderQuantity);
        Assert.Equal(1, offer.DeliveryDaysMin);
        Assert.Equal("RUB", offer.Price.Currency);
    }

    [Fact]
    public void MapApi_RequiresArticleAndBrandMatch()
    {
        var row = new MoskvorechieApiRow("04114", "Metalcaucho", "Прокладка", "gid1", "2 дня", 327m, 0, 100, 1);

        Assert.NotNull(MoskvorechieMapper.MapApi(row, "04114", null));
        Assert.NotNull(MoskvorechieMapper.MapApi(row, "04-114", "METALCAUCHO"));
        Assert.Null(MoskvorechieMapper.MapApi(row, "04114", "Zekkert"));      // API мог вернуть чужой бренд
        Assert.Null(MoskvorechieMapper.MapApi(row, "77777", null));
        Assert.Equal(">100", MoskvorechieMapper.MapApi(row, "04114", null)!.StockText);   // остатка нет — ступень «под заказ»
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<string> Calls = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add($"{request.Method} {request.RequestUri!.PathAndQuery}");
            return respond(request, body);
        }
    }

    private static HttpResponseMessage Page(string text) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(System.Text.Encoding.GetEncoding(1251).GetBytes(text)) };

    static MoskvorechieTests() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    [Fact]
    public async Task Portal_LogsIn_SearchesAndParses_Windows1251()
    {
        var handler = new ScriptedHandler((r, _) => r.RequestUri!.AbsolutePath switch
        {
            "/login.lmz" => Page("ok"),
            "/search.lmz" => Page(SearchHtml),
            _ => Page("{\"body\":\"" + PriceHtml.Replace("\"", "\\\"").Replace("\n", "") + "\"}")
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://portal.moskvorechie.ru/") };
        var client = new MoskvorechiePortalClient(http, Options.Create(new MoskvorechieOptions { Login = "l", PortalPassword = "p" }));

        var offers = await client.SearchAsync("04114", "Metalcaucho", default);

        Assert.Equal(2, offers.Count);
        Assert.Equal("Запад", offers[0].Warehouse);               // кириллица из windows-1251
        Assert.Contains(handler.Calls, c => c.StartsWith("POST /search.lmz"));
        Assert.Contains(handler.Calls, c => c.Contains("show_price.aj") && c.Contains("gid=4444"));
    }

    [Fact]
    public async Task Api_PostsCredentialsInBody_AndParsesRows()
    {
        string? sentBody = null;
        string? sentUri = null;
        var handler = new ScriptedHandler((r, body) =>
        {
            sentBody = body;
            sentUri = r.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"result":[{"nr":"04114","brand":"Metalcaucho","price":"327","stock":"2","sorder":"0","minq":"1","delivery":"2 дня","gid":"g1"}]}""")
            };
        });
        var client = new MoskvorechieApiClient(new HttpClient(handler),
            Options.Create(new MoskvorechieOptions { BaseUrl = "https://portal.moskvorechie.ru/portal.api", Login = "lg", ApiKey = "key123" }));

        var rows = await client.SearchPricesAsync("04114", default);

        var row = Assert.Single(rows);
        Assert.Equal(327m, row.Price);
        Assert.Equal(2, row.Stock);
        Assert.Contains("p=key123", sentBody);
        Assert.DoesNotContain("key123", sentUri);
    }

    [Fact]
    public async Task Api_ErrorField_Throws()
    {
        var handler = new ScriptedHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"error":"bad key"}""") });
        var client = new MoskvorechieApiClient(new HttpClient(handler), Options.Create(new MoskvorechieOptions { BaseUrl = "https://x/portal.api", Login = "l", ApiKey = "k" }));

        var ex = await Assert.ThrowsAsync<MoskvorechieApiException>(() => client.SearchPricesAsync("X", default));
        Assert.Equal("bad key", ex.Message);
    }
}
