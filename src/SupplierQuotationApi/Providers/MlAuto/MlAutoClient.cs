using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupplierQuotationApi.Providers.MlAuto;

public sealed class MlAutoApiException(string message) : Exception(message);

public sealed class MlAutoOffer
{
    [JsonPropertyName("PIN")] public string? Pin { get; set; }
    [JsonPropertyName("BRAND")] public string? Brand { get; set; }
    [JsonPropertyName("NAME")] public string? Name { get; set; }
    [JsonPropertyName("QUANTITY")] public string? Quantity { get; set; }
    [JsonPropertyName("PRICE")] public string? Price { get; set; }
    /// <summary>yyyyMMddHHmmss, время UTC+3.</summary>
    [JsonPropertyName("DATE")] public string? Date { get; set; }
    [JsonPropertyName("STORAGE_CODE")] public string? StorageCode { get; set; }
    /// <summary>Вероятность поставки, %.</summary>
    [JsonPropertyName("CHANCE")] public string? Chance { get; set; }
    [JsonPropertyName("MIN")] public string? Min { get; set; }
    /// <summary>«NO» — оригинал, иначе аналог.</summary>
    [JsonPropertyName("ANALOG")] public string? Analog { get; set; }
    [JsonPropertyName("RETURN_PERIOD")] public string? ReturnPeriod { get; set; }
}

public sealed class MlAutoImporter
{
    [JsonPropertyName("ID")] public string? Id { get; set; }
    /// <summary>Условия поставки (текст, местами с HTML).</summary>
    [JsonPropertyName("DESCR")] public string? Description { get; set; }
}

/// <summary>Клиент ML-Auto webservice: только form-urlencoded POST, логин и пароль в теле запроса (не в URL).
/// Ответ — {STATUS, MESSAGES, RESPONSE}; тело начинается с BOM, который ломает разбор JSON.</summary>
public sealed class MlAutoClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly MlAutoOptions _options;

    public MlAutoClient(HttpClient http, MlAutoOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <summary>Поиск по артикулу и бренду. Бренд обязателен: без него API отвечает «Необходимо указать Бренд».</summary>
    public Task<List<MlAutoOffer>> SearchAsync(string article, string brand, CancellationToken cancellationToken) =>
        PostAsync<MlAutoOffer>("Search/", new() { ["ARTICLE"] = article, ["BRAND"] = brand, ["SEARCH_TYPE"] = "0" }, cancellationToken);

    /// <summary>Справочник поставщиков: ID склада → условия поставки.</summary>
    public Task<List<MlAutoImporter>> GetImportersAsync(CancellationToken cancellationToken) =>
        PostAsync<MlAutoImporter>("ImportersSearch/", [], cancellationToken);

    private async Task<List<T>> PostAsync<T>(string path, Dictionary<string, string> parameters, CancellationToken ct)
    {
        var form = new Dictionary<string, string>(parameters) { ["LOGIN"] = _options.Login!, ["PASSWORD"] = _options.Password! };
        using var response = await _http.PostAsync(path, new FormUrlEncodedContent(form), ct);
        if (!response.IsSuccessStatusCode)
            throw new MlAutoApiException($"ML-Auto вернул HTTP {(int)response.StatusCode}.");

        var text = (await response.Content.ReadAsStringAsync(ct)).TrimStart('﻿');
        JsonElement root;
        try { root = JsonSerializer.Deserialize<JsonElement>(text, Json); }
        catch (JsonException) { throw new MlAutoApiException("ML-Auto вернул невалидный JSON."); }

        var status = root.TryGetProperty("STATUS", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 200;
        if (status != 200)
        {
            var message = MessageText(root);
            throw new MlAutoApiException(string.IsNullOrWhiteSpace(message) ? $"ML-Auto вернул статус {status}." : message);
        }

        // «Ничего не найдено» приходит со статусом 200, MSG_TEXT и пустым RESPONSE — это пустой результат, а не ошибка.
        return root.TryGetProperty("RESPONSE", out var body) && body.ValueKind == JsonValueKind.Array
            ? body.Deserialize<List<T>>(Json) ?? []
            : [];
    }

    /// <summary>MESSAGES — массив или объект с ERROR_TEXT / MSG_TEXT.</summary>
    private static string? MessageText(JsonElement root)
    {
        if (!root.TryGetProperty("MESSAGES", out var messages)) return null;
        var parts = new List<string>();
        void Collect(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String: parts.Add(e.GetString()!); break;
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) Collect(p.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray()) Collect(item);
                    break;
            }
        }
        Collect(messages);
        return string.Join("; ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }
}
