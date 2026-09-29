using System.Net;
using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.Moskvorechie;

public static class MoskvorechieExtensions
{
    public static IServiceCollection AddMoskvorechie(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MoskvorechieOptions>(configuration.GetSection(MoskvorechieOptions.Section));

        // Сессия портала живёт в cookie: контейнер общий и переживает ротацию обработчиков фабрики.
        var cookies = new CookieContainer();
        services.AddProviderHttpClient(MoskvorechiePortalClient.HttpClientName, $"{MoskvorechieOptions.Section}:PortalUrl", cookies)
            .ConfigureHttpClient(c => c.BaseAddress ??= new Uri(MoskvorechieOptions.DefaultPortalUrl));
        services.AddTransient(sp => new MoskvorechiePortalClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(MoskvorechiePortalClient.HttpClientName),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<MoskvorechieOptions>>()));

        services.AddProviderHttpClient<MoskvorechieApiClient, MoskvorechieApiClient>($"{MoskvorechieOptions.Section}:BaseUrl");
        services.AddSingleton<IQuotationProvider, MoskvorechieProvider>();
        return services;
    }
}
