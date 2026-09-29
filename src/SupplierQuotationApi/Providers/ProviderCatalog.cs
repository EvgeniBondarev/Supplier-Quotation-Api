namespace SupplierQuotationApi.Providers;

/// <summary>Паспорт поставщика: как он устроен и что нужно учитывать клиенту. Один источник для Swagger и <c>GET /api/quotations/providers</c>.</summary>
/// <param name="Protocol">Транспорт и формат API.</param>
/// <param name="Authentication">Способ авторизации сервиса у поставщика.</param>
/// <param name="Currency">Валюта цен поставщика (<c>price.currency</c>).</param>
/// <param name="BrandRequired">Без бренда поставщик отвечает статусом <c>Error</c>.</param>
/// <param name="SupportsAnalogs">Учитывает флаг <c>includeAnalogs</c>. Иначе всегда только оригиналы.</param>
/// <param name="IpWhitelist">Доступ ограничен по IP клиента на стороне поставщика.</param>
/// <param name="RateLimit">Ограничение частоты запросов, если есть.</param>
/// <param name="Notes">Особенности, важные для клиента.</param>
/// <param name="DocsUrl">Официальная документация, если ссылка известна.</param>
/// <param name="ApiUrl">Адрес API поставщика.</param>
public sealed record ProviderMetadata(
    string Protocol,
    string Authentication,
    string Currency,
    bool BrandRequired,
    bool SupportsAnalogs,
    bool IpWhitelist,
    string? RateLimit,
    IReadOnlyList<string> Notes,
    string? DocsUrl,
    string? ApiUrl);

/// <summary>Каталог паспортов по ключам провайдеров. Тест проверяет, что у каждого зарегистрированного провайдера есть запись.</summary>
public static class ProviderCatalog
{
    private static readonly IReadOnlyList<string> Rub = [];

    public static readonly IReadOnlyDictionary<string, ProviderMetadata> All = new Dictionary<string, ProviderMetadata>(StringComparer.OrdinalIgnoreCase)
    {
        ["ShateM"] = new("REST (JSON)", "Bearer-токен по логину и паролю", "BYN", false, true, false, null,
            ["Бренд в поиске артикулов у поставщика обязателен, поэтому сервис ищет по коду (все бренды) и выбирает нужный локально.",
             "Названия складов берутся из справочника (кэш 30 минут).",
             "Цены в белорусских рублях пересчитываются по курсу ЦБ РФ."],
            "https://api-doc.shate-m.ru/", "https://api.shate-m.by"),

        ["Armtek"] = new("REST (form-urlencoded, конверт STATUS/MESSAGES/RESP)", "HTTP Basic", "RUB", false, true, false, null,
            ["Российский аккаунт находит предложения только при заданном адресе доставки (KUNNR_ZA).",
             "Есть суточная квота запросов: после её исчерпания аккаунт блокируется на 30 минут.",
             "Каноническая пара артикул и бренд берётся из ассортимента (assortment_search), без бренда опрашиваются все бренды кода (до 6)."],
            "https://ws.armtek.ru/", "https://ws.armtek.ru"),
        ["ArmtekBy"] = new("REST (form-urlencoded, конверт STATUS/MESSAGES/RESP)", "HTTP Basic", "BYN", false, true, false, null,
            ["Белорусский аккаунт Армтек (сбытовая организация 2000); цены в BYN пересчитываются по курсу ЦБ РФ.",
             "Общая квота запросов, как у российского аккаунта."],
            "https://ws.armtek.ru/", "https://ws.armtek.ru"),

        ["FavoritParts"] = FavoritParts("Союз"),
        ["FavoritPartsIstra"] = FavoritParts("Истра"),
        ["FavoritPartsRostov"] = FavoritParts("Ростов"),

        ["ForumAuto"] = ForumAuto("Союз"),
        ["ForumAutoInterparts"] = ForumAuto("Интерпартс"),
        ["ForumAutoPiter"] = ForumAuto("Питер"),
        ["ForumAutoRostov"] = ForumAuto("Ростов"),
        ["ForumAutoIstra"] = ForumAuto("Истра"),

        ["Avd"] = new("SOAP 1.1", "Логин и пароль в теле запроса", "RUB", false, false, false, null,
            ["Агрегатор: в каждой строке свой поставщик и регион, склад в ответе — «поставщик (регион)».",
             "offerId — Hash предложения, он же нужен для добавления в корзину АВД.",
             "С брендом берётся совпавший каталог, без бренда опрашиваются все (до 6). Если бренд задан и совпадений нет — пусто."],
            "https://www.avdmotors.ru/p/web-service-pokupatelya", "https://ws1.avdmotors.ru"),

        ["Berg"] = new("REST v1.0 (JSON)", "Ключ API в заголовке X-Berg-API-Key", "RUB", false, true, false, null,
            ["Параметр brand_name API игнорирует и отдаёт все бренды артикула: фильтр по бренду локальный, с транслитерацией (LUKOIL ↔ ЛУКОЙЛ).",
             "Сроки доставки зависят от адреса отгрузки: используется адрес из настроек или первый активный адрес аккаунта."],
            null, "https://api.berg.ru"),

        ["Motex"] = new("REST (JSON, конверт status/messages/response)", "Bearer-токен по логину и паролю", "BYN", false, false, true, null,
            ["Работает только с IP из белого списка клиента; с других адресов статус Error «Нет доступа. Проверьте IP адрес».",
             "404 от поставщика означает «нет предложений», а не ошибку.",
             "Остаток вида «20>» означает «от 20 шт.»."],
            null, "https://api.motexc.by"),

        ["MlAuto"] = new("REST (form-urlencoded)", "Логин и пароль в теле запроса", "BYN", true, false, false, null,
            ["Ищет только по паре артикул + бренд; бренд нужен в точном написании: пробуются исходное, очищенное (WYNN'S → WYNNS) и алиасы.",
             "Срок считается до склада в Минске, для российского заказа его нужно проверять отдельно."],
            null, "https://www.ml-auto.by/webservice"),
        ["MlAutoRu"] = new("REST (form-urlencoded)", "Логин и пароль в теле запроса", "RUB", true, false, false, null,
            ["Российский контур ML-Auto; тот же API и те же ограничения по бренду, что у белорусского."],
            null, "https://ml-auto.ru/webservice"),

        ["MoskvorechieIstra"] = new("HTML-портал (cookie, windows-1251) и запасной JSON API", "Вход на портал; ключ API для запасного пути", "RUB", false, false, false, null,
            ["Основной путь — портал: склад, остаток, срок, кратность.",
             "Запасной JSON API вызывается только без бренда: с брендом он способен вернуть чужого производителя.",
             "Запросы к порталу идут по очереди (одна сессия на аккаунт)."],
            null, "https://portal.moskvorechie.ru"),

        ["ProfitLiga"] = new("REST v1.4 (JSON)", "Ключ API в параметре secret", "RUB", false, false, false, null,
            ["С брендом используется /search/crosses без замен, без бренда — /search/items.",
             "Срок приходит в часах и переводится в дни округлением вверх."],
            null, "https://api.pr-lg.ru"),

        ["Japarts"] = new("HTTP GET (JSON, windows-1251)", "Логин и пароль в параметрах запроса", "RUB", false, false, false, null,
            ["API подставляет значения в SQL без экранирования: артикул с кавычкой отклоняется, небезопасный бренд не отправляется.",
             "Если поиск с брендом пуст, выполняется поиск без бренда и локальная фильтрация."],
            null, "https://www.japarts.ru"),

        ["Nikei"] = new("REST (JSON)", "HTTP Basic", "RUB", false, false, false, null,
            ["Бренд сопоставляется нестрого; если поиск пуст, используется список брендов номера.",
             "Дубли с разными токенами корзины схлопываются по product_key."],
            null, "https://nikei.ru"),

        ["MikadoMskHod20"] = new("SOAP 1.1 (ASMX)", "ClientID и пароль в теле запроса", "RUB", true, false, true, null,
            ["Ищет только по паре артикул + бренд.",
             "Message, отличный от Ok, — ошибка (неверный логин или namespace); пустой список с Ok — «нет предложений».",
             "Доступ ограничен по IP клиента."],
            null, "https://www.mikado-parts.ru/ws1"),

        ["ZZapMoscow"] = new("REST (JSON)", "Ключ API в заголовке zzap-api-key", "RUB", true, false, false,
            "не чаще 1 запроса в 3,5 с на весь ключ",
            ["Агрегатор продавцов: warehouse — «продавец · точка выдачи»; отдаются 20 самых дешёвых предложений.",
             "Запросы идут по очереди; при превышении лимита 429, затем капча.",
             "Правила ZZap требуют показывать источник данных: подпись есть в priceNote и providerData.attribution.",
             "Предложения со сроком «не указан» (254/255) и б/у не включаются."],
            "https://wiki.zzap.ru/category/api-new/", "https://b52-api.zzap.pro"),
    };

    private static ProviderMetadata FavoritParts(string account) => new("REST (JSON)", "Ключи key и developerKey в параметрах запроса", "RUB", false, true, false, null,
        [$"Аккаунт «{account}»; ключ покупателя отдаётся в accountLogin только началом.",
         "Бренд в API передаётся только вместе с аналогами: без них сервис берёт все бренды артикула и фильтрует локально.",
         "Срок считается по московской дате."],
        null, "https://api.favorit-parts.ru");

    private static ProviderMetadata ForumAuto(string account) => new("REST v2 (JSON)", "Логин и пароль в параметрах запроса", "RUB", false, true, false, null,
        [$"Аккаунт «{account}». Каталог общий для всех аккаунтов, склады, цены и сроки — свои.",
         "Бренд в API не передаётся (нужно точное написание): берутся все бренды артикула и фильтруются локально.",
         "«Товары не найдены» (FaultCode 27) — пустой результат, а не ошибка."],
        null, "https://api.forum-auto.ru/v2");

    /// <summary>Паспорт поставщика. Неизвестный ключ — <c>null</c>.</summary>
    public static ProviderMetadata? Find(string key) => All.TryGetValue(key, out var metadata) ? metadata : null;
}
