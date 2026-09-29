using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Infrastructure.Swagger;
using SupplierQuotationApi.Providers;

namespace SupplierQuotationApi.Tests;

/// <summary>Документация Swagger сверяется с кодом: ключи поставщиков, примеры, схема безопасности и закоммиченный docs/openapi.json.</summary>
public class OpenApiTests
{
    [ModuleInitializer]
    internal static void IsolateFromDeveloperEnv() => Environment.SetEnvironmentVariable("SKIP_DOTENV", "1");

    private static WebApplicationFactory<Program> Factory(string environment = "Development", bool swagger = false, string? apiKey = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("App:SwaggerEnabled", swagger.ToString());
            b.UseSetting("App:ApiKey", apiKey ?? "test-key");
            b.UseSetting("Studio2Db:ConnectionString", "");
        });

    private static async Task<JsonNode> LoadDocumentAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        return JsonNode.Parse(json)!;
    }

    [Fact]
    public async Task Document_DescribesAllOperations_WithSecurityAndTags()
    {
        using var factory = Factory();
        var doc = await LoadDocumentAsync(factory);

        Assert.Equal("Supplier Quotation API", (string)doc["info"]!["title"]!);
        var paths = doc["paths"]!.AsObject().Select(p => p.Key).Order().ToArray();
        Assert.Equal(["/api/quotations", "/api/quotations/providers", "/api/quotations/providers/{providerKey}", "/api/quotations/stream"], paths);

        var scheme = doc["components"]!["securitySchemes"]!["ApiKey"]!;
        Assert.Equal("apiKey", (string)scheme["type"]!);
        Assert.Equal("X-Api-Key", (string)scheme["name"]!);
        Assert.Equal("header", (string)scheme["in"]!);
        Assert.Equal("ApiKey", doc["security"]![0]!.AsObject().Single().Key);

        foreach (var (path, item) in doc["paths"]!.AsObject())
            foreach (var (method, op) in item!.AsObject())
            {
                Assert.False(string.IsNullOrWhiteSpace((string?)op!["summary"]), $"{method} {path}: нет summary");
                Assert.NotEmpty(op["tags"]!.AsArray());
                Assert.True(op["responses"]!["401"] is not null, $"{method} {path}: не описан ответ 401");
            }
    }

    [Fact]
    public async Task Document_ListsEveryRegisteredProviderKey_InRequestEnum_AndCatalogCoversAll()
    {
        using var factory = Factory();
        var registered = factory.Services.GetServices<IQuotationProvider>().Select(p => p.Key).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        // Каталог паспортов и реестр провайдеров не расходятся: новый поставщик без паспорта ломает этот тест.
        Assert.Equal(registered, ProviderCatalog.All.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray());

        var doc = await LoadDocumentAsync(factory);
        var documented = doc["components"]!["schemas"]!["QuotationRequest"]!["properties"]!["providers"]!["items"]!["enum"]!
            .AsArray().Select(x => (string)x!).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(registered, documented);
    }

    [Fact]
    public async Task Document_TakesFieldDescriptionsFromXmlComments()
    {
        using var factory = Factory();
        var doc = await LoadDocumentAsync(factory);
        var article = doc["components"]!["schemas"]!["QuotationRequest"]!["properties"]!["article"]!;

        Assert.Contains("Разделители и регистр", (string)article["description"]!);
        Assert.Equal("K1223A", (string)article["example"]!);
        Assert.Contains("Итог опроса", (string)doc["components"]!["schemas"]!["QuotationStatus"]!["description"]!);
    }

    [Fact]
    public async Task Document_SingleProviderOperations_ListKeysAndExamples()
    {
        using var factory = Factory();
        var doc = await LoadDocumentAsync(factory);
        var item = doc["paths"]!["/api/quotations/providers/{providerKey}"]!;

        Assert.NotNull(item["get"]);
        Assert.NotNull(item["post"]);
        foreach (var method in new[] { "get", "post" })
        {
            var op = item[method]!;
            var key = op["parameters"]!.AsArray().Single(p => (string)p!["name"]! == "providerKey")!;
            Assert.Equal("path", (string)key["in"]!);
            Assert.Equal(ProviderCatalog.All.Count, key["schema"]!["enum"]!.AsArray().Count);
            Assert.NotNull(op["responses"]!["404"]);
            Assert.Equal(["error", "noOffers", "ok"], op["responses"]!["200"]!["content"]!["application/json"]!["examples"]!.AsObject().Select(x => x.Key).Order().ToArray());
        }

        var queryNames = item["get"]!["parameters"]!.AsArray().Select(p => (string)p!["name"]!).Order().ToArray();
        Assert.Equal(["article", "brand", "includeAnalogs", "providerKey"], queryNames);
        Assert.Equal(["byBrand", "noBrand", "withAnalogs"], item["post"]!["requestBody"]!["content"]!["application/json"]!["examples"]!.AsObject().Select(x => x.Key).Order().ToArray());
    }

    [Fact]
    public async Task Document_Stream_ReturnsNdjsonOnly_AndErrorsAsJson()
    {
        using var factory = Factory();
        var doc = await LoadDocumentAsync(factory);
        var responses = doc["paths"]!["/api/quotations/stream"]!["post"]!["responses"]!;

        Assert.Equal(["application/x-ndjson"], responses["200"]!["content"]!.AsObject().Select(x => x.Key).ToArray());
        Assert.Equal(["application/json"], responses["400"]!["content"]!.AsObject().Select(x => x.Key).ToArray());
        Assert.NotNull(responses["200"]!["content"]!["application/x-ndjson"]!["examples"]!["stream"]);
    }

    [Fact]
    public async Task Examples_MatchTheRealContract_SoTheyCannotDrift()
    {
        using var factory = Factory();
        var doc = await LoadDocumentAsync(factory);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        var post = doc["paths"]!["/api/quotations"]!["post"]!;

        foreach (var (name, example) in post["requestBody"]!["content"]!["application/json"]!["examples"]!.AsObject())
        {
            var request = example!["value"].Deserialize<QuotationRequest>(options);
            Assert.False(string.IsNullOrWhiteSpace(request?.Article), $"пример запроса {name}");
        }
        foreach (var (name, example) in post["responses"]!["200"]!["content"]!["application/json"]!["examples"]!.AsObject())
        {
            var response = example!["value"].Deserialize<QuotationResponse>(options);
            Assert.NotEmpty(response!.Providers);
            Assert.All(response.Providers.SelectMany(p => p.Offers), o => Assert.True(o.Price.Amount > 0, $"пример ответа {name}"));
        }

        var list = doc["paths"]!["/api/quotations/providers"]!["get"]!["responses"]!["200"]!["content"]!["application/json"]!["examples"]!["list"]!["value"];
        Assert.All(list.Deserialize<List<ProviderInfo>>(options)!, p => Assert.True(ProviderCatalog.All.ContainsKey(p.Key)));
    }

    [Fact]
    public void ExampleOffers_KeepInvariants()
    {
        foreach (var provider in SwaggerExamples.ResponseOk.Providers.Concat(SwaggerExamples.ResponseMixed.Providers))
        {
            Assert.True(ProviderCatalog.All.ContainsKey(provider.ProviderKey));
            if (provider.Status != QuotationStatus.Ok) Assert.Empty(provider.Offers);
            if (provider.Status is QuotationStatus.Error or QuotationStatus.Timeout or QuotationStatus.Disabled) Assert.NotNull(provider.Error);
            foreach (var offer in provider.Offers)
                Assert.True(offer.DeliveryDaysMax >= offer.DeliveryDaysMin, "верхняя граница срока не меньше нижней");
        }
    }

    [Fact]
    public async Task Document_MatchesCommittedFile()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var actual = Normalize(await client.GetStringAsync("/swagger/v1/swagger.json"));

        var path = Path.Combine(RepositoryRoot(), "docs", "openapi.json");
        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
        {
            File.WriteAllText(path, actual + "\n");
            return;
        }

        Assert.True(File.Exists(path), "docs/openapi.json нет: выполните тесты с UPDATE_OPENAPI=1");
        Assert.True(actual == Normalize(File.ReadAllText(path)),
            "docs/openapi.json устарел. Обновите: UPDATE_OPENAPI=1 dotnet test --filter Document_MatchesCommittedFile");
    }

    [Fact]
    public async Task Swagger_HiddenInProduction_UnlessEnabled()
    {
        using (var hidden = Factory("Production", swagger: false).CreateClient())
            Assert.Equal(HttpStatusCode.NotFound, (await hidden.GetAsync("/swagger/v1/swagger.json")).StatusCode);

        using (var open = Factory("Production", swagger: true).CreateClient())
        {
            Assert.Equal(HttpStatusCode.OK, (await open.GetAsync("/swagger/v1/swagger.json")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await open.GetAsync("/swagger/index.html")).StatusCode);
        }
    }

    [Fact]
    public async Task Api_RequiresKey_AndProvidersEndpointReturnsPassports()
    {
        using var factory = Factory("Production", apiKey: "secret-test-key");
        using var client = factory.CreateClient();

        var denied = await client.GetAsync("/api/quotations/providers");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);   // health без ключа

        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-test-key");
        var providers = await client.GetFromJsonAsync<List<ProviderInfo>>("/api/quotations/providers",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(ProviderCatalog.All.Count, providers!.Count);
        Assert.All(providers, p =>
        {
            Assert.NotNull(p.Protocol);
            Assert.NotNull(p.Currency);
            Assert.NotEmpty(p.Notes);
        });
        Assert.True(providers.Single(p => p.Key == "ZZapMoscow").BrandRequired);
        Assert.True(providers.Single(p => p.Key == "Motex").IpWhitelist);
        Assert.False(providers.Single(p => p.Key == "Avd").SupportsAnalogs);
        Assert.All(providers, p => Assert.False(p.IsEnabled));      // .env в тестах не читается: ни один поставщик не настроен
    }

    [Fact]
    public async Task Quote_UnknownProvider_Returns400WithProblemDetails()
    {
        using var factory = Factory("Production", apiKey: "secret-test-key");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-test-key");

        var response = await client.PostAsJsonAsync("/api/quotations", new { article = "K1223A", providers = new[] { "Foo" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Foo", await response.Content.ReadAsStringAsync());
    }

    private static string Normalize(string json) => JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).ReplaceLineEndings("\n");

    private static string RepositoryRoot([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
