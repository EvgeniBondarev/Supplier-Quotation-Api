using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.ProfitLiga;

public static class ProfitLigaExtensions
{
    public static IServiceCollection AddProfitLiga(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProfitLigaOptions>(configuration.GetSection(ProfitLigaOptions.Section));
        services.AddProviderHttpClient<ProfitLigaClient, ProfitLigaClient>($"{ProfitLigaOptions.Section}:BaseUrl");
        services.AddSingleton<IQuotationProvider, ProfitLigaProvider>();
        return services;
    }
}
