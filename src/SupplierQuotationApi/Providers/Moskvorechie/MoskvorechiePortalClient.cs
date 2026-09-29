using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Moskvorechie;

/// <summary>Read-only клиент внутренних страниц портала: search.lmz → gid товара, show_price.aj → строки цен.
/// Сессия держится в cookie (общий контейнер), вход — под замком; страницы в windows-1251.</summary>
public sealed class MoskvorechiePortalClient
{
    public const string HttpClientName = "MoskvorechieIstraPortal";
    private static readonly Encoding PortalEncoding = CreateEncoding();
    private readonly HttpClient _http;
    private readonly MoskvorechieOptions _options;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static volatile bool _authenticated;

    public MoskvorechiePortalClient(HttpClient http, IOptions<MoskvorechieOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MoskvorechiePortalOffer>> SearchAsync(string article, string? brand, CancellationToken ct,
        ProducerAliasMap? aliases = null)
    {
        // Портал ведёт одну сессию на аккаунт: запросы идут по очереди.
        await Gate.WaitAsync(ct);
        try
        {
            await AuthenticateAsync(ct);

            using var searchContent = new FormUrlEncodedContent(new Dictionary<string, string> { ["sv"] = article, ["dv"] = "1" });
            var search = await SendAsync(HttpMethod.Post, "search.lmz", searchContent, "поиске товара", ct);
            var gid = FindGid(search, article, brand, aliases);
            if (gid is null) return [];

            var price = await SendAsync(HttpMethod.Get,
                $"show_price.aj?i4&cat_id=2&gid={gid.Value}&hash={Random.Shared.NextDouble().ToString(CultureInfo.InvariantCulture)}",
                null, "получении цен", ct);
            return ParseOffers(ExtractBody(price), article, brand);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        if (_authenticated) return;
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = _options.Login!, ["password"] = _options.PortalPassword!, ["come_from"] = "/index.lmz", ["mp"] = "1"
        });
        await SendAsync(HttpMethod.Post, "login.lmz", content, "авторизации", ct);
        _authenticated = true;
    }

    private async Task<string> SendAsync(HttpMethod method, string endpoint, HttpContent? content, string action, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, endpoint) { Content = content };
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _authenticated = false;
            throw new MoskvorechieApiException($"Портал Москворечье вернул HTTP {(int)response.StatusCode} при {action}.");
        }

        var text = PortalEncoding.GetString(await response.Content.ReadAsByteArrayAsync(ct));
        // Истёкшая сессия отдаёт страницу входа: сбрасываем флаг, следующий запрос войдёт заново.
        if (endpoint != "login.lmz" && text.Contains("Вход на портал", StringComparison.OrdinalIgnoreCase))
        {
            _authenticated = false;
            throw new MoskvorechieApiException("Сессия портала Москворечье истекла, повторите запрос.");
        }
        return text;
    }

    /// <summary>Ссылка good_info.lmc с точным артикулом; бренд портала пишется в заголовке перед артикулом, сверяем по вхождению.</summary>
    public static long? FindGid(string html, string article, string? brand, ProducerAliasMap? aliases = null)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var links = document.DocumentNode.SelectNodes("//a[contains(@href, 'good_info.lmc')]")?.AsEnumerable() ?? [];
        var requested = Normalize(article);
        aliases ??= ProducerAliasMap.Empty;
        var requestedBrand = Normalize(brand);
        // «KYB» портала и «Kayaba» заказа — один бренд: сверяем все известные написания.
        var variants = aliases.GetVariants(brand);

        foreach (var link in links)
        {
            if (Normalize(link.InnerText) != requested) continue;

            var header = Normalize(link.ParentNode?.InnerText);
            var index = header.IndexOf(requested, StringComparison.Ordinal);
            var candidateBrand = index > 0 ? header[..index] : string.Empty;
            if (requestedBrand.Length > 0 && !variants.Any(v =>
                    candidateBrand.Contains(v, StringComparison.Ordinal) ||
                    (candidateBrand.Length > 0 && v.Contains(candidateBrand, StringComparison.Ordinal))))
                continue;

            var href = WebUtility.HtmlDecode(link.GetAttributeValue("href", ""));
            var query = System.Web.HttpUtility.ParseQueryString(new Uri("https://portal.moskvorechie.ru/" + href.TrimStart('/')).Query);
            if (long.TryParse(query["gid"], out var gid)) return gid;
        }
        return null;
    }

    /// <summary>Ответ show_price.aj: JSON (иногда с ведущей «1») с HTML в поле body.</summary>
    public static string ExtractBody(string raw)
    {
        try
        {
            var json = raw.TrimStart();
            if (json.StartsWith('1')) json = json[1..];
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            throw new MoskvorechieApiException("Не удалось разобрать ответ портала Москворечье.");
        }
    }

    public static IReadOnlyList<MoskvorechiePortalOffer> ParseOffers(string html, string article, string? requestedBrand)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var result = new List<MoskvorechiePortalOffer>();
        var rows = document.DocumentNode.SelectNodes("//tr[.//a[contains(@href, 'to_basket_gid=')]]")?.AsEnumerable() ?? [];

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("./td")?.Select(x => WebUtility.HtmlDecode(x.InnerText).Trim()).ToList() ?? [];
            var href = row.SelectSingleNode(".//a[contains(@href, 'to_basket_gid=')]")?.GetAttributeValue("href", "") ?? string.Empty;
            var query = System.Web.HttpUtility.ParseQueryString(new Uri("https://portal.moskvorechie.ru/" + WebUtility.HtmlDecode(href).TrimStart('/')).Query);
            var offerId = query["to_basket_gid"];

            if (string.IsNullOrWhiteSpace(offerId) || cells.Count < 10 ||
                !Normalize(cells[2]).Contains(Normalize(article), StringComparison.Ordinal)) continue;
            if (!int.TryParse(cells[8], out var min) || ParsePrice(cells[9]) is not { } price) continue;

            var brand = string.IsNullOrWhiteSpace(requestedBrand) ? cells[1] : requestedBrand.Trim();
            result.Add(new MoskvorechiePortalOffer(offerId, brand, cells[2], cells[3], cells[4], cells[6], cells[7], min, price));
        }
        return result;
    }

    /// <summary>«327 руб.», «3026.88», «1 234,50 руб.»: пробелы и «руб.» убираются; запятая с 1–2 цифрами в конце — десятичная,
    /// иначе разделитель тысяч. Строгий разбор с InvariantCulture читал «1 234,50» как 123450.</summary>
    public static decimal? ParsePrice(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Replace("руб.", "", StringComparison.OrdinalIgnoreCase);
        text = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

        if (text.Contains(',') && text.Contains('.')) text = text.Replace(",", "");
        else if (System.Text.RegularExpressions.Regex.IsMatch(text, @",\d{1,2}$")) text = text.Replace(',', '.');
        else text = text.Replace(",", "");

        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ? price : null;
    }

    public static string Normalize(string? value) =>
        new(value?.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray() ?? []);

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251);
    }
}
