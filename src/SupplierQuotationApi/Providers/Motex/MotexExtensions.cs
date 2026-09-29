using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.Motex;

public static class MotexExtensions
{
    public static IServiceCollection AddMotex(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MotexOptions>(configuration.GetSection(MotexOptions.Section));
        services.AddSingleton<MotexTokenCache>();
        services.AddProviderHttpClient<MotexClient, MotexClient>($"{MotexOptions.Section}:BaseUrl");
        services.AddSingleton<IQuotationProvider, MotexProvider>();
        return services;
    }
}
