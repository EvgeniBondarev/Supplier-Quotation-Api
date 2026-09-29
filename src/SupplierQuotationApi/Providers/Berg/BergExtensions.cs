using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.Berg;

public static class BergExtensions
{
    public static IServiceCollection AddBerg(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BergOptions>(configuration.GetSection(BergOptions.Section));
        services.AddProviderHttpClient<BergClient, BergClient>($"{BergOptions.Section}:BaseUrl");
        services.AddSingleton<IQuotationProvider, BergProvider>();
        return services;
    }
}
