using SupplierQuotationApi.Providers.Armtek;

namespace SupplierQuotationApi.Tests;

public class ArmtekMapperTests
{
    // 2026-10-01 08:30 UTC = 11:30 по Москве.
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 8, 30, 0, TimeSpan.Zero);

    private static ArmtekSearchItem Item(string? artId = "1174075", string keyzak = "0000170915", string? price = "1385.75",
        string? rvalue = "2", string? dlvdt = "20261002113000") => new()
    {
        ArtId = artId, Keyzak = keyzak, PartnerWarehouseCode = "245863", Pin = "SF-2364", Brand = "ZEKKERT",
        Name = "Пружина", Price = price, Currency = "RUB", Rvalue = rvalue, DeliveryDate = dlvdt, MinQuantity = "1", Analog = ""
    };

    private static readonly IReadOnlyDictionary<string, string> Stores =
        new Dictionary<string, string> { ["0000170915"] = "ЦЗ Москва" };

    [Theory]
    [InlineData("5", 5, "5")]
    [InlineData(">20", 20, ">20")]
    [InlineData("меньше 100", 100, "<100")]
    [InlineData("больше 50", 50, ">50")]
    public void Stock_ParsesNumbersAndWords(string raw, int stock, string text) =>
        Assert.Equal((stock, text), ArmtekMapper.Stock(raw));

    [Fact]
    public void Stock_WithoutNumber_KeepsText() =>
        Assert.Equal((null, "под заказ"), ArmtekMapper.Stock("под заказ"));

    [Fact]
    public void DeliveryDays_UsesMoscowTime()
    {
        // Ровно +1 день по Москве.
        Assert.Equal(1, ArmtekMapper.DeliveryDays("20261002113000", Now));
        // Через сутки и час → округление вверх.
        Assert.Equal(2, ArmtekMapper.DeliveryDays("20261002123000", Now));
        Assert.Equal(0, ArmtekMapper.DeliveryDays("20260930113000", Now));
        Assert.Null(ArmtekMapper.DeliveryDays("bad", Now));
        Assert.Null(ArmtekMapper.DeliveryDays(null, Now));
    }

    [Fact]
    public void Map_FillsUnifiedOffer()
    {
        var offer = ArmtekMapper.Map(Item(), Stores, "4000", "10000001", "20000002", Now, 1385.75m)!;

        Assert.Equal("ЦЗ Москва (0000170915)", offer.Warehouse);
        Assert.Equal(1385.75m, offer.Price.Amount);
        Assert.Equal("RUB", offer.Price.Currency);
        Assert.Equal(2, offer.Stock);
        Assert.Equal(1, offer.DeliveryDaysMin);
        Assert.Equal("20000002", offer.ProviderData!["deliveryKunnr"]);
        Assert.Equal("false", offer.ProviderData["isAnalog"]);
    }

    [Fact]
    public void Map_OfferId_IsUniquePerWarehouse_EvenWithSameArtId()
    {
        var a = ArmtekMapper.Map(Item(keyzak: "A"), Stores, "4000", null, null, Now, null)!;
        var b = ArmtekMapper.Map(Item(keyzak: "B"), Stores, "4000", null, null, Now, null)!;

        Assert.NotEqual(a.OfferId, b.OfferId);
    }

    [Fact]
    public void Map_WithoutPrice_ReturnsNull() =>
        Assert.Null(ArmtekMapper.Map(Item(price: null), Stores, "4000", null, null, Now, null));
}
