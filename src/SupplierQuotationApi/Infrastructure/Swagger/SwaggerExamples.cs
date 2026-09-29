using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Infrastructure.Swagger;

/// <summary>Примеры запросов и ответов для Swagger. Значения взяты из реальных ответов поставщиков, логины и токены заменены.</summary>
public static class SwaggerExamples
{
    public static readonly QuotationRequest RequestByBrand = new() { Article = "K1223A", Brand = "Filtron" };

    public static readonly QuotationRequest RequestSelected = new()
    {
        Article = "W28510", Brand = "WYNN'S", Providers = ["ShateM", "Armtek", "ZZapMoscow"]
    };

    public static readonly QuotationRequest RequestWithAnalogs = new() { Article = "K1223A", Brand = "Filtron", IncludeAnalogs = true };

    public static readonly QuotationRequest RequestNoBrand = new() { Article = "K1223A" };

    public static readonly ProviderQuotationRequest SingleByBrand = new() { Article = "K1223A", Brand = "Filtron" };

    public static readonly ProviderQuotationRequest SingleWithAnalogs = new() { Article = "K1223A", Brand = "Filtron", IncludeAnalogs = true };

    public static readonly ProviderQuotationRequest SingleNoBrand = new() { Article = "K1223A" };

    private static QuotationOffer ShateMOffer => new()
    {
        OfferId = "035C2148256848A9336DCAFF21E9FAA6…",
        ProductName = "Присадка в топливо (дизель)",
        Brand = "WYNNS",
        Article = "W28510",
        Description = "Присадка для дизельного топлива WYNNS DIESEL ADDITIVE 250мл.",
        Warehouse = "Привольный (Центральный склад Привольный)",
        Stock = 8, StockText = "8", MinOrderQuantity = 1, DeliveryDaysMin = 2, DeliveryDaysMax = 2,
        Price = new Money(82.62m, "BYN"), PriceRub = new Money(2304.84m, "RUB"),
        PriceNote = "Возврат невозможен. Для подбора запчастей рекомендуем использовать каталоги производителей.",
        ProviderData = new Dictionary<string, string?>
        {
            ["articleId"] = "242434472", ["locationCode"] = "SHATE-S01", ["agreementCode"] = "<код договора>",
            ["priceHash"] = "1661401022", ["multiplicity"] = "1", ["supplyRating"] = "0"
        }
    };

    private static QuotationOffer ArmtekOffer => new()
    {
        OfferId = "13283294|0000137666|260756",
        ProductName = "WDA-Wynn's Diesel Additive 250 мл комплексная присадка в дизельное топливо",
        Brand = "WYNN'S", Article = "W28510",
        Warehouse = "ЦЗ Москва (0000137666)",
        Stock = 10, StockText = "10", MinOrderQuantity = 1, DeliveryDaysMin = 2, DeliveryDaysMax = 2,
        Price = new Money(2283.03m, "RUB"), PriceRub = new Money(2283.03m, "RUB"),
        ProviderData = new Dictionary<string, string?>
        {
            ["artId"] = "13283294", ["keyzak"] = "0000137666", ["probability"] = "73", ["returnDays"] = "14", ["isAnalog"] = "false"
        }
    };

    private static QuotationOffer ZZapOffer => new()
    {
        OfferId = "1240683544109553744",
        ProductName = "Присадка в топливо дизельная 250 мл",
        Brand = "WYNNS", Article = "W28510",
        Warehouse = "G-kamion (Коньково) · Москва, м.Коньково",
        Stock = 400, StockText = "400", MinOrderQuantity = 1, DeliveryDaysMin = 7, DeliveryDaysMax = 7,
        Price = new Money(1481m, "RUB"), PriceRub = new Money(1481m, "RUB"),
        PriceNote = "Самовывоз · Прайс обновлён: 1ч. назад · Информация о запчастях предоставлена системой ZZap",
        ProviderData = new Dictionary<string, string?>
        {
            ["codeDocB"] = "1240683544109553744", ["sellerName"] = "G-kamion (Коньково)", ["sellerRating"] = "4",
            ["codeRegion"] = "1", ["minSumOrder"] = "0", ["pack"] = "1", ["legalOnly"] = "false",
            ["attribution"] = "Информация о запчастях предоставлена системой ZZap"
        }
    };

    private static ProviderQuotation Provider(string key, string name, string logo, string login, QuotationStatus status,
        long ms, string? error = null, params QuotationOffer[] offers) => new()
    {
        ProviderKey = key, ProviderName = name, LogoUrl = logo, AccountLogin = login, Status = status, Error = error,
        DurationMs = ms, Offers = offers
    };

    public static readonly ProviderQuotation ProviderOk = Provider("Armtek", "Армтек", "http://localhost:5099/logos/armtek.svg",
        "<логин учётной записи>", QuotationStatus.Ok, 620, null, ArmtekOffer);

    public static readonly QuotationResponse ResponseOk = new()
    {
        Article = "W28510", Brand = "WYNN'S", RequestedAtUtc = new DateTime(2026, 9, 29, 8, 57, 39, DateTimeKind.Utc), DurationMs = 3607,
        Providers =
        [
            Provider("Armtek", "Армтек", "http://localhost:5099/logos/armtek.svg", "<логин учётной записи>", QuotationStatus.Ok, 620, null, ArmtekOffer),
            Provider("ShateM", "Шате-М", "http://localhost:5099/logos/shatem.svg", "<логин учётной записи>", QuotationStatus.Ok, 1733, null, ShateMOffer),
            Provider("ZZapMoscow", "ZZap Москва", "http://localhost:5099/logos/zzap.svg", "api-key", QuotationStatus.Ok, 3607, null, ZZapOffer)
        ]
    };

    /// <summary>Пример результата одного поставщика с ошибкой: ZZap без бренда.</summary>
    public static readonly ProviderQuotation ProviderError = Provider("ZZapMoscow", "ZZap Москва", "http://localhost:5099/logos/zzap.svg", "api-key",
        QuotationStatus.Error, 17, "ZZap ищет только по паре артикул + бренд: укажите бренд.");

    /// <summary>Пример результата одного поставщика без предложений.</summary>
    public static readonly ProviderQuotation ProviderNoOffers = Provider("Berg", "Берг", "http://localhost:5099/logos/berg.png", "api-key",
        QuotationStatus.NoOffers, 118);

    /// <summary>Разные исходы у разных поставщиков в одном ответе: ошибка одного не ломает остальных.</summary>
    public static readonly QuotationResponse ResponseMixed = new()
    {
        Article = "K1223A", Brand = null, RequestedAtUtc = new DateTime(2026, 9, 29, 9, 10, 2, DateTimeKind.Utc), DurationMs = 20014,
        Providers =
        [
            Provider("Berg", "Берг", "http://localhost:5099/logos/berg.png", "api-key", QuotationStatus.NoOffers, 118),
            Provider("Motex", "МоТехС", "http://localhost:5099/logos/motex.png", "<логин учётной записи>", QuotationStatus.Error, 238,
                "Нет доступа. Проверьте IP адрес.(текущий IP: 203.0.113.10)"),
            Provider("ZZapMoscow", "ZZap Москва", "http://localhost:5099/logos/zzap.svg", "api-key", QuotationStatus.Error, 17,
                "ZZap ищет только по паре артикул + бренд: укажите бренд."),
            Provider("Nikei", "Nikei", "http://localhost:5099/logos/nikei.svg", "<логин учётной записи>", QuotationStatus.Timeout, 20000,
                "Поставщик не ответил за 20 с."),
            Provider("Japarts", "Japarts", "http://localhost:5099/logos/japarts.jpg", "<логин учётной записи>", QuotationStatus.Disabled, 0,
                "Поставщик выключен: не заполнены настройки в .env.")
        ]
    };

    public static readonly ProviderInfo ProviderInfoShateM = new()
    {
        Key = "ShateM", Name = "Шате-М", IsEnabled = true, AccountLogin = "<логин учётной записи>",
        LogoUrl = "http://localhost:5099/logos/shatem.svg",
        Protocol = "REST (JSON)", Authentication = "Bearer-токен по логину и паролю", Currency = "BYN",
        BrandRequired = false, SupportsAnalogs = true, IpWhitelist = false,
        Notes = ["Бренд в поиске артикулов у поставщика обязателен, поэтому сервис ищет по коду (все бренды) и выбирает нужный локально."],
        DocsUrl = "https://api-doc.shate-m.ru/", ApiUrl = "https://api.shate-m.by"
    };

    public static readonly ProviderInfo ProviderInfoZZap = new()
    {
        Key = "ZZapMoscow", Name = "ZZap Москва", IsEnabled = true, AccountLogin = "api-key",
        LogoUrl = "http://localhost:5099/logos/zzap.svg",
        Protocol = "REST (JSON)", Authentication = "Ключ API в заголовке zzap-api-key", Currency = "RUB",
        BrandRequired = true, SupportsAnalogs = false, IpWhitelist = false, RateLimit = "не чаще 1 запроса в 3,5 с на весь ключ",
        Notes = ["Агрегатор продавцов: warehouse — «продавец · точка выдачи»; отдаются 20 самых дешёвых предложений."],
        DocsUrl = "https://wiki.zzap.ru/category/api-new/", ApiUrl = "https://b52-api.zzap.pro"
    };

    public static readonly IReadOnlyList<ProviderInfo> ProviderList = [ProviderInfoShateM, ProviderInfoZZap];

    /// <summary>Пример 404: такого поставщика нет.</summary>
    public static readonly object NotFound = new { title = "Неизвестный поставщик: Foo", status = 404 };

    /// <summary>Пример 400: неизвестный ключ поставщика.</summary>
    public static readonly object BadRequest = new { title = "Неизвестные поставщики: Foo, Bar", status = 400 };

    /// <summary>Пример 401: нет или неверен ключ.</summary>
    public static readonly object Unauthorized = new { title = "Нужен корректный заголовок X-Api-Key.", status = 401 };
}
