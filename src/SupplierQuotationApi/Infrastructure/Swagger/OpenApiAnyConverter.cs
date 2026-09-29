using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi.Any;

namespace SupplierQuotationApi.Infrastructure.Swagger;

/// <summary>Превращает C#-объект в значение примера OpenAPI: объекты-примеры пишутся типами контракта, а не вручную JSON-ом.</summary>
public static class OpenApiAnyConverter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions NoNullOptions = new(Options)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <param name="ignoreNulls">Не выводить null-поля: примеры запросов без пустых необязательных полей. В ответах null оставлен: он показывает, что поле бывает пустым.</param>
    public static IOpenApiAny From(object value, bool ignoreNulls = false) =>
        Convert(JsonSerializer.SerializeToNode(value, value.GetType(), ignoreNulls ? NoNullOptions : Options));

    private static IOpenApiAny Convert(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return new OpenApiNull();
            case JsonObject obj:
                var result = new OpenApiObject();
                foreach (var (key, child) in obj) result[key] = Convert(child);
                return result;
            case JsonArray array:
                var list = new OpenApiArray();
                foreach (var child in array) list.Add(Convert(child));
                return list;
            case JsonValue v when v.TryGetValue<bool>(out var b):
                return new OpenApiBoolean(b);
            case JsonValue v when v.TryGetValue<long>(out var l):
                return l is >= int.MinValue and <= int.MaxValue ? new OpenApiInteger((int)l) : new OpenApiLong(l);
            case JsonValue v when v.TryGetValue<decimal>(out var d):
                return new OpenApiDouble((double)d);
            case JsonValue v when v.TryGetValue<DateTime>(out var dt):
                return new OpenApiDateTime(new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)));
            default:
                return new OpenApiString(node.ToString());
        }
    }
}
