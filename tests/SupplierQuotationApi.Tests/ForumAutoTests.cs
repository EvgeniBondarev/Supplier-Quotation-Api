using System.Net;
using SupplierQuotationApi.Infrastructure;
using SupplierQuotationApi.Providers.ForumAuto;

namespace SupplierQuotationApi.Tests;

public class ForumAutoTests
{
    private static ForumAutoRow Row(string art = "K1223A", string brand = "FILTRON", decimal? price = 1048.7m, string gid = "G1",
        int days = 2, int? hours = 45, int? returnable = 0) => new()
    {
        Gid = gid, Art = art, Brand = brand, Name = "Фильтр салона", DeliveryDays = days, DeliveryHours = hours,
        Multiplicity = 1, Stock = 5, Price = price, Warehouse = "MSK-CD", IsReturnable = returnable
    };

    [Fact]
    public void Map_FillsUnifiedOffer()
    {
        var offer = Assert.Single(ForumAutoMapper.Map([Row()], "K1223A", "Filtron", false));

        Assert.Equal("G1", offer.OfferId);
        Assert.Equal("MSK-CD", offer.Warehouse);
        Assert.Equal(1048.7m, offer.Price.Amount);
        Assert.Equal("RUB", offer.Price.Currency);
        Assert.Equal(5, offer.Stock);
        Assert.Equal("Возврат не разрешён", offer.PriceNote);
        Assert.Equal("false", offer.ProviderData!["returnable"]);
    }

    [Fact]
    public void Map_DeliveryMax_UsesTotalHoursRoundedUp_NotAddedToDays()
    {
        // 165 ч ≈ 6,9 дня → верхняя граница 7, а не 7 + 7 = 14, как выходило при сложении дней и часов.
        var offer = Assert.Single(ForumAutoMapper.Map([Row(days: 7, hours: 165)], "K1223A", null, false));
        Assert.Equal(7, offer.DeliveryDaysMin);
        Assert.Equal(7, offer.DeliveryDaysMax);

        var noHours = Assert.Single(ForumAutoMapper.Map([Row(days: 3, hours: null)], "K1223A", null, false));
        Assert.Equal(3, noHours.DeliveryDaysMax);
    }

    [Fact]
    public void Map_FiltersBrandLocally_WithFuzzyMatch()
    {
        var rows = new[] { Row(brand: "FEBI", gid: "A"), Row(brand: "PRC", gid: "B") };

        Assert.Equal("A", Assert.Single(ForumAutoMapper.Map(rows, "K1223A", "Febi Bilstein", false)).OfferId);
        Assert.Equal(2, ForumAutoMapper.Map(rows, "K1223A", null, false).Count);
        Assert.Empty(ForumAutoMapper.Map(rows, "K1223A", "Zekkert", false));
    }

    [Fact]
    public void Map_OtherArticles_OnlyWithAnalogs_AndMarked()
    {
        var rows = new[] { Row(), Row(art: "K-9999", gid: "X") };

        Assert.Single(ForumAutoMapper.Map(rows, "K1223A", null, false));

        var withAnalogs = ForumAutoMapper.Map(rows, "K1223A", "Filtron", true);
        Assert.Equal(2, withAnalogs.Count);
        Assert.Equal("true", withAnalogs.Single(x => x.OfferId == "X").ProviderData!["isAnalog"]);
    }

    [Fact]
    public void Map_SkipsRowsWithoutPriceOrGid()
    {
        Assert.Empty(ForumAutoMapper.Map([Row(price: null), Row(gid: "")], "K1223A", null, false));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static (ForumAutoClient, StubHandler) Client(string body)
    {
        var handler = new StubHandler(body);
        var http = new HttpClient(handler) { BaseAddress = HttpClientExtensions.ParseBaseAddress("https://api.forum-auto.ru/v2") };
        return (new ForumAutoClient(http, new ForumAutoOptions { Login = "l", Password = "p" }), handler);
    }

    [Fact]
    public async Task Client_KeepsVersionSegmentInUrl_AndSendsCredentials()
    {
        var (client, handler) = Client("""[{"gid":"G1","art":"K1223A","brand":"FILTRON","price":10}]""");

        var rows = await client.ListGoodsAsync("K1223A", false, default);

        Assert.Single(rows);
        Assert.StartsWith("https://api.forum-auto.ru/v2/listgoods?", handler.LastUri);
        Assert.Contains("login=l", handler.LastUri);
        Assert.Contains("cross=0", handler.LastUri);
    }

    [Fact]
    public async Task Client_FaultCode27_IsEmptyResult()
    {
        var (client, _) = Client("""{"errors":{"FaultCode":27,"FaultString":"Товары не найдены."}}""");

        Assert.Empty(await client.ListGoodsAsync("X", false, default));
    }

    [Fact]
    public async Task Client_OtherFault_Throws()
    {
        var (client, _) = Client("""{"errors":{"FaultCode":5,"FaultString":"Неверный логин"}}""");

        var ex = await Assert.ThrowsAsync<ForumAutoApiException>(() => client.ListGoodsAsync("X", false, default));
        Assert.Equal("Неверный логин", ex.Message);
    }

    [Theory]
    [InlineData("https://api.forum-auto.ru/v2", "https://api.forum-auto.ru/v2/")]
    [InlineData("https://ws.armtek.ru/", "https://ws.armtek.ru/")]
    public void ParseBaseAddress_AlwaysEndsWithSlash(string input, string expected) =>
        Assert.Equal(expected, HttpClientExtensions.ParseBaseAddress(input)!.ToString());
}
