using System.Net;
using SupplierQuotationApi.Core;
using SupplierQuotationApi.Providers.MlAuto;

namespace SupplierQuotationApi.Tests;

public class MlAutoTests
{
    // 2026-09-29 22:00 UTC = 30.09 01:00 в UTC+3.
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 22, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, string> NoConditions = new Dictionary<string, string>();

    private static MlAutoOffer Offer(string? price = "29.16", string? quantity = "200", string? date = "20261001121232",
        string storage = "300", string returnPeriod = "30") => new()
    {
        Pin = "K1223A", Brand = "FILTRON", Name = "Фильтр салонный", Price = price, Quantity = quantity, Date = date,
        StorageCode = storage, Chance = "100", Min = "1", Analog = "NO", ReturnPeriod = returnPeriod
    };

    [Fact]
    public void Map_FillsUnifiedOffer()
    {
        var conditions = new Dictionary<string, string> { ["300"] = "Заказы до 17:00 — доставка сегодня" };

        var offer = MlAutoMapper.Map(Offer(), "Минск", "BYN", conditions, Now, 813.47m)!;

        Assert.Equal("Минск 300", offer.Warehouse);
        Assert.Equal(29.16m, offer.Price.Amount);
        Assert.Equal("BYN", offer.Price.Currency);
        Assert.Equal(813.47m, offer.PriceRub!.Amount);
        Assert.Equal(200, offer.Stock);
        Assert.Equal(1, offer.DeliveryDaysMin);   // 01.10 − 30.09 (по UTC+3)
        Assert.Equal("Возврат: 30 дн. Заказы до 17:00 — доставка сегодня", offer.PriceNote);
        Assert.Equal("100", offer.ProviderData!["supplyChance"]);
    }

    [Theory]
    [InlineData("2", 2, "2")]
    [InlineData(">10", 10, ">10")]
    [InlineData("20>", 20, ">20")]
    [InlineData(" 219,00", 219, "219")]
    public void Stock_ParsesAllFormats(string raw, int stock, string text) =>
        Assert.Equal((stock, text), MlAutoMapper.Stock(raw));

    [Fact]
    public void DeliveryDays_CountsFromLocalDate_NotUtc()
    {
        Assert.Equal(1, MlAutoMapper.DeliveryDays("20261001000000", Now));
        Assert.Equal(0, MlAutoMapper.DeliveryDays("20250101000000", Now));
        Assert.Null(MlAutoMapper.DeliveryDays("bad", Now));
    }

    [Fact]
    public void Map_WithoutPrice_ReturnsNull_AndCleanTextStripsHtml()
    {
        Assert.Null(MlAutoMapper.Map(Offer(price: null), "Минск", "BYN", NoConditions, Now, null));
        Assert.Equal("Доставка завтра", MlAutoMapper.CleanText("<b>Доставка</b> <br/>завтра")!.Replace("  ", " "));
    }

    private sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? LastUri;
        public string? LastBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.ToString();
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static (MlAutoClient, StubHandler) Client(string body)
    {
        var handler = new StubHandler(body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://www.ml-auto.by/webservice/") };
        return (new MlAutoClient(http, new MlAutoOptions { Login = "lg", Password = "pw" }), handler);
    }

    [Fact]
    public async Task Client_HandlesBom_SendsCredentialsInBodyOnly()
    {
        var (client, handler) = Client("﻿{\"STATUS\":200,\"MESSAGES\":[],\"RESPONSE\":[{\"PIN\":\"K1223A\",\"PRICE\":\"29.16\",\"ANALOG\":\"NO\"}]}");

        var offers = await client.SearchAsync("K1223A", "FILTRON", default);

        Assert.Single(offers);
        Assert.Contains("ARTICLE=K1223A", handler.LastBody);
        Assert.Contains("LOGIN=lg", handler.LastBody);
        Assert.DoesNotContain("pw", handler.LastUri);
        Assert.EndsWith("/webservice/Search/", handler.LastUri);
    }

    [Fact]
    public async Task Client_NothingFound_IsEmpty_AndErrorStatusThrows()
    {
        var (empty, _) = Client("{\"STATUS\":200,\"MESSAGES\":{\"MSG_TEXT\":\"По запросу ничего не найдено.\"},\"RESPONSE\":[]}");
        Assert.Empty(await empty.SearchAsync("X", "Y", default));

        var (failing, _) = Client("{\"STATUS\":400,\"MESSAGES\":{\"ERROR_TEXT\":\"Ошибка! Необходимо указать Бренд.\"},\"RESPONSE\":[]}");
        var ex = await Assert.ThrowsAsync<MlAutoApiException>(() => failing.SearchAsync("X", "Y", default));
        Assert.Equal("Ошибка! Необходимо указать Бренд.", ex.Message);
    }

    [Theory]
    [InlineData("motex.png", "http://h/logos/motex.png")]
    [InlineData("https://www.ml-auto.by/logo.png", "https://www.ml-auto.by/logo.png")]
    [InlineData(null, null)]
    public void ResolveLogoUrl_SupportsLocalFilesAndExternalLinks(string? file, string? expected) =>
        Assert.Equal(expected, QuotationService.ResolveLogoUrl(file, "http://h"));
}
