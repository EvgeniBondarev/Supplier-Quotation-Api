using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.Avd;

public static class AvdExtensions
{
    public static IServiceCollection AddAvd(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AvdOptions>(configuration.GetSection(AvdOptions.Section));
        services.AddProviderHttpClient<AvdClient, AvdClient>($"{AvdOptions.Section}:BaseUrl");
        services.AddSingleton<IQuotationProvider, AvdProvider>();
        return services;
    }
}
