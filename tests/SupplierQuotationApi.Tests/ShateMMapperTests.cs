using SupplierQuotationApi.Providers.ShateM;

namespace SupplierQuotationApi.Tests;

public class ShateMMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly ShateMArticle Article = new(1, "OC90", "MAHLE", "Фильтр масляный", "");

    private static ShateMPrice Price(decimal? value = 13.08m, string currency = "BYN", ShateMQuantity? quantity = null,
        List<ShateMDelivery>? delivery = null, DateTimeOffset? shipping = null) =>
        new("id1", 1, "SHATE-S01", "SHATE-M02", "AGR", new ShateMPriceValue(value, currency),
            quantity ?? new ShateMQuantity(100, "Equal", 1, 1), new ShateMAddInfo("Возврат невозможен.<br>Ок", null), 42,
            delivery, shipping, new ShateMSupplyProbability(90));

    [Theory]
    [InlineData(10, "MoreThan", 10, ">10")]
    [InlineData(5, "LessThan", 5, "<5")]
    [InlineData(7, "Equal", 7, "7")]
    public void Stock_ParsesAvailabilityType(int available, string type, int stock, string text) =>
        Assert.Equal((stock, text), ShateMMapper.Stock(new ShateMQuantity(available, type, 1, 1)));

    [Fact]
    public void DeliveryDays_UsesDeliveryDates_NotShipping()
    {
        var price = Price(delivery: [new(Now.AddDays(3).AddHours(1)), new(Now.AddDays(6))], shipping: Now.AddDays(1));

        Assert.Equal((4, 6), ShateMMapper.DeliveryDays(price, Now));
    }

    [Fact]
    public void DeliveryDays_FallsBackToShipping_AndNullWhenNothing()
    {
        Assert.Equal((2, 2), ShateMMapper.DeliveryDays(Price(shipping: Now.AddDays(2)), Now));
        Assert.Equal((null, null), ShateMMapper.DeliveryDays(Price(), Now));
    }

    [Fact]
    public void Map_FillsUnifiedOffer()
    {
        var locations = new Dictionary<string, ShateMLocation>(StringComparer.OrdinalIgnoreCase)
            { ["SHATE-S01"] = new("SHATE-S01", "Центральный склад", "Привольный") };

        var offer = ShateMMapper.Map(Article, Price(delivery: [new(Now.AddDays(3))]), locations, Now, 364.89m)!;

        Assert.Equal("Привольный (Центральный склад)", offer.Warehouse);
        Assert.Equal(13.08m, offer.Price.Amount);
        Assert.Equal("BYN", offer.Price.Currency);
        Assert.Equal(364.89m, offer.PriceRub!.Amount);
        Assert.Equal("Возврат невозможен. Ок", offer.PriceNote);
        Assert.Equal("AGR", offer.ProviderData!["agreementCode"]);
        Assert.Equal("42", offer.ProviderData["priceHash"]);
    }

    [Fact]
    public void Map_WithoutPrice_ReturnsNull() =>
        Assert.Null(ShateMMapper.Map(Article, Price(value: null), new Dictionary<string, ShateMLocation>(), Now, null));
}
