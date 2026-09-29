using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.Mikado;

public static class MikadoExtensions
{
    public static IServiceCollection AddMikado(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MikadoOptions>(configuration.GetSection(MikadoOptions.Section));
        services.AddProviderHttpClient<MikadoClient, MikadoClient>($"{MikadoOptions.Section}:BaseUrl");
        services.AddSingleton<IQuotationProvider, MikadoProvider>();
        return services;
    }
}
