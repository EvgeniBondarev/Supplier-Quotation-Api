using Microsoft.Extensions.DependencyInjection;

namespace SupplierQuotationApi.Infrastructure;

public static class HttpClientExtensions
{
    /// <summary>Единая настройка HTTP-клиента поставщика: пул соединений, retry, circuit breaker, таймауты.
    /// Все провайдеры регистрируют клиентов только через этот метод.</summary>
    public static IHttpClientBuilder AddProviderHttpClient<TClient, TImplementation>(
        this IServiceCollection services, string baseUrlConfigKey)
        where TClient : class
        where TImplementation : class, TClient
    {
        var builder = services.AddHttpClient<TClient, TImplementation>((sp, client) =>
        {
            var baseUrl = sp.GetRequiredService<IConfiguration>()[baseUrlConfigKey];
            client.BaseAddress = ParseBaseAddress(baseUrl);
        });

        return builder.ConfigureProviderHandlers();
    }

    /// <summary>То же для именованного клиента: когда у одного API несколько аккаунтов и клиент создаётся через фабрику.</summary>
    /// <param name="cookies">Общий контейнер cookie для API с сессией (портал): переживает ротацию обработчиков фабрики.</param>
    public static IHttpClientBuilder AddProviderHttpClient(this IServiceCollection services, string name, string baseUrlConfigKey,
        System.Net.CookieContainer? cookies = null)
    {
        return services.AddHttpClient(name, (sp, client) =>
        {
            var baseUrl = sp.GetRequiredService<IConfiguration>()[baseUrlConfigKey];
            client.BaseAddress = ParseBaseAddress(baseUrl);
        }).ConfigureProviderHandlers(cookies);
    }

    /// <summary>Завершающий слэш обязателен: без него относительный путь заменяет последний сегмент
    /// базового адреса (https://host/v2 + listgoods даёт https://host/listgoods, а не /v2/listgoods).</summary>
    public static Uri? ParseBaseAddress(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        var normalized = baseUrl.Trim().TrimEnd('/') + "/";
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static IHttpClientBuilder ConfigureProviderHandlers(this IHttpClientBuilder builder, System.Net.CookieContainer? cookies = null)
    {
        builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            UseCookies = cookies is not null,
            CookieContainer = cookies ?? new System.Net.CookieContainer()
        });

        builder.AddStandardResilienceHandler(o =>
        {
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(18);
            o.Retry.MaxRetryAttempts = 1;
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        });
        return builder;
    }
}
