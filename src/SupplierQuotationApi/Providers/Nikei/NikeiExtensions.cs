using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.Nikei;

public static class NikeiExtensions
{
    public static IServiceCollection AddNikei(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NikeiOptions>(configuration.GetSection(NikeiOptions.Section));
        services.AddProviderHttpClient<NikeiClient, NikeiClient>($"{NikeiOptions.Section}:BaseUrl");
        services.AddSingleton<IQuotationProvider, NikeiProvider>();
        return services;
    }
}
