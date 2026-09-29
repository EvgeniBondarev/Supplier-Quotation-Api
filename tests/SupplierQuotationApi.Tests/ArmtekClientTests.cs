using System.Net;
using SupplierQuotationApi.Providers.Armtek;

namespace SupplierQuotationApi.Tests;

public class ArmtekClientTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls;
        public string? LastAuth;
        public string? LastBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastAuth = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static (ArmtekClient Client, StubHandler Handler) Make(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://ws.armtek.ru/") };
        return (new ArmtekClient(http, new ArmtekOptions { User = "u", Password = "p" }, TimeProvider.System), handler);
    }

    private static readonly Dictionary<string, string> Form = new() { ["PIN"] = "X" };

    [Fact]
    public async Task Search_ParsesItems_SendsBasicAuthAndForm()
    {
        var (client, handler) = Make(HttpStatusCode.OK,
            """{"STATUS":200,"MESSAGES":[],"RESP":[{"PIN":"A","BRAND":"B","PRICE":12.5,"RVALUE":3,"KEYZAK":"K1"}]}""");

        var items = await client.PostAsync<List<ArmtekSearchItem>>("ws_search/search", Form, default);

        var item = Assert.Single(items!);
        Assert.Equal("12.5", item.Price);   // число → строка
        Assert.Equal("3", item.Rvalue);
        Assert.StartsWith("Basic ", handler.LastAuth);
        Assert.Contains("format=json", handler.LastBody);
    }

    [Fact]
    public async Task NothingFound_RespObject_ReturnsNull_NotError()
    {
        var (client, _) = Make(HttpStatusCode.OK, """{"STATUS":200,"MESSAGES":[],"RESP":{"MSG":"ничего не найдено"}}""");

        Assert.Null(await client.PostAsync<List<ArmtekSearchItem>>("ws_search/search", Form, default));
    }

    [Fact]
    public async Task BusinessError_ThrowsWithMessageText()
    {
        var (client, _) = Make(HttpStatusCode.OK, """{"STATUS":200,"MESSAGES":[{"TYPE":"E","TEXT":"Неверный KUNNR_RG"}],"RESP":null}""");

        var ex = await Assert.ThrowsAsync<ArmtekApiException>(() => client.PostAsync<List<ArmtekSearchItem>>("ws_search/search", Form, default));
        Assert.Equal("Неверный KUNNR_RG", ex.Message);
    }

    [Fact]
    public async Task QuotaExceeded_BlocksFurtherRequests()
    {
        var (client, handler) = Make(HttpStatusCode.Unauthorized, """{"MESSAGES":[{"TEXT":"Превышено количество запросов для данного логина в сутки"}]}""");

        await Assert.ThrowsAsync<ArmtekApiException>(() => client.PostAsync<List<ArmtekSearchItem>>("ws_search/search", Form, default));
        var ex = await Assert.ThrowsAsync<ArmtekApiException>(() => client.PostAsync<List<ArmtekSearchItem>>("ws_search/search", Form, default));

        Assert.Contains("суточное", ex.Message);
        Assert.Equal(1, handler.Calls); // второй запрос до сети не дошёл
    }
}
