using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SupplierQuotationApi.Providers.ZZap;

/// <summary>Ошибка ZZap. <see cref="StatusCode"/> 429 — превышена частота запросов, а не сбой запроса.</summary>
public sealed class ZZapApiException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

/// <summary>Предложение продавца из /search/light. ZZap — агрегатор: каждая строка — отдельный продавец.</summary>
public sealed class ZZapOffer
{
    [JsonPropertyName("code_doc_b")] public string? CodeDocB { get; set; }
    [JsonPropertyName("partnumber")] public string? PartNumber { get; set; }
    [JsonPropertyName("class_man")] public string? Brand { get; set; }
    [JsonPropertyName("class_cat")] public string? Name { get; set; }
    [JsonPropertyName("class_user")] public string? Seller { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("descr_address")] public string? Address { get; set; }
    [JsonPropertyName("user_key")] public string? SellerKey { get; set; }
    [JsonPropertyName("price_v2")] public decimal? Price { get; set; }
    /// <summary>Остаток. Отрицательный — продавец подтверждает наличие, не раскрывая количество.</summary>
    [JsonPropertyName("qty_v2")] public int? Quantity { get; set; }
    [JsonPropertyName("descr_qty_v2")] public string? QuantityText { get; set; }
    [JsonPropertyName("qty_max")] public int? QuantityMax { get; set; }
    [JsonPropertyName("pack")] public int? Pack { get; set; }
    [JsonPropertyName("min_sum_order")] public decimal? MinSumOrder { get; set; }
    /// <summary>Срок в днях; 254/255 — «срок не указан».</summary>
    [JsonPropertyName("delivery_days")] public int? DeliveryDays { get; set; }
    [JsonPropertyName("descr_delivery")] public string? DeliveryText { get; set; }
    [JsonPropertyName("used_v2")] public bool? Used { get; set; }
    [JsonPropertyName("wholesale_v2")] public bool? Wholesale { get; set; }
    [JsonPropertyName("courier")] public bool? Courier { get; set; }
    [JsonPropertyName("type_price")] public string? TypePrice { get; set; }
    [JsonPropertyName("descr_type_price")] public string? TypePriceText { get; set; }
    [JsonPropertyName("apply")] public string? Apply { get; set; }
    [JsonPropertyName("shipment")] public string? Shipment { get; set; }
    [JsonPropertyName("descr_price_date")] public string? PriceAge { get; set; }
    [JsonPropertyName("price_date")] public string? PriceDate { get; set; }
    [JsonPropertyName("rating_total")] public int? Rating { get; set; }
    [JsonPropertyName("descr_rating_total_count")] public string? RatingCount { get; set; }
}

/// <summary>Клиент ZZap API. Ключ — заголовком zzap-api-key, в URL его нет. ZZap считает частоту по ключу, а не по соединению,
/// поэтому все запросы процесса идут через общий троттлинг (singleton <see cref="ZZapThrottle"/>): по одному, с паузой.</summary>
public sealed class ZZapClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int TooManyRequests = 429;

    private readonly HttpClient _http;
    private readonly ZZapOptions _options;
    private readonly ZZapThrottle _throttle;

    public ZZapClient(HttpClient http, IOptions<ZZapOptions> options, ZZapThrottle throttle)
    {
        _http = http;
        _options = options.Value;
        _throttle = throttle;
    }

    /// <summary>/search/light, type_request=5: только запрошенный номер и только новые детали — единственный режим,
    /// укладывающийся в контракт «оригиналы без кроссов». Бренд обязателен: без него API отдаёт пустой список.</summary>
    public async Task<List<ZZapOffer>> SearchLightAsync(string article, string brand, CancellationToken ct)
    {
        var path = "api/client/v1/search/light" +
                   $"?partnumber={Uri.EscapeDataString(article.Trim())}&class_man={Uri.EscapeDataString(brand.Trim())}" +
                   $"&type_request=5&code_region={_options.CodeRegion}&page=1&page_size=100";

        return await _throttle.RunAsync(async () =>
        {
            var (payload, status) = await SendOnceAsync(path, ct);
            // Лимит скользящий, и первый запрос после полной паузы обычно проходит; второй 429 подряд — ключ забанен или капча.
            if (status == TooManyRequests)
            {
                await _throttle.WaitSlotAsync(ct);
                (payload, status) = await SendOnceAsync(path, ct);
            }

            EnsureSuccess(payload, status);
            return payload.TryGetProperty("result", out var result) && result.TryGetProperty("data", out var data) &&
                   data.ValueKind == JsonValueKind.Array
                ? data.Deserialize<List<ZZapOffer>>(Json) ?? []
                : [];
        }, ct);
    }

    private async Task<(JsonElement Payload, int Status)> SendOnceAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("zzap-api-key", _options.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await _http.SendAsync(request, ct);
        var status = (int)response.StatusCode;
        var text = await response.Content.ReadAsStringAsync(ct);
        try { return (JsonSerializer.Deserialize<JsonElement>(text, Json), status); }
        catch (JsonException) { throw new ZZapApiException($"ZZap вернул невалидный JSON (HTTP {status}).", status); }
    }

    /// <summary>ZZap дублирует HTTP-статус в поле code, а причину кладёт в errors: {"поле": "текст"}.
    /// success=false приходит и с HTTP 200, поэтому проверяются оба признака.</summary>
    private static void EnsureSuccess(JsonElement payload, int status)
    {
        var success = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        var code = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetInt32() : status;
        if (success && status is >= 200 and < 300) return;

        var effective = status == TooManyRequests || code == TooManyRequests ? TooManyRequests : (code != 200 ? code : status);
        var errors = new List<string>();
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
            errors.AddRange(e.EnumerateObject().Select(p => p.Value.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));

        var message = effective == TooManyRequests
            ? "ZZap: превышена максимальная частота запросов, повторите позже."
            : errors.Count > 0 ? $"ZZap: {string.Join("; ", errors)}" : $"ZZap вернул HTTP {status}.";
        throw new ZZapApiException(message, effective);
    }
}

/// <summary>Общий для процесса троттлинг ZZap: один запрос за раз, пауза от конца предыдущего ответа.</summary>
public sealed class ZZapThrottle
{
    private readonly ZZapOptions _options;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastFinished = DateTimeOffset.MinValue;

    public ZZapThrottle(IOptions<ZZapOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await WaitSlotAsync(ct);
            return await action();
        }
        finally
        {
            _lastFinished = _clock.GetUtcNow();
            _gate.Release();
        }
    }

    public async Task WaitSlotAsync(CancellationToken ct)
    {
        var wait = TimeSpan.FromMilliseconds(_options.MinRequestIntervalMs) - (_clock.GetUtcNow() - _lastFinished);
        if (wait > TimeSpan.Zero) await Task.Delay(wait, _clock, ct);
    }
}
