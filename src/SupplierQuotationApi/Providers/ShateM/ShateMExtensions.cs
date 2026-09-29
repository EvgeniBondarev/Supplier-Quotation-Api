using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.ShateM;

public static class ShateMExtensions
{
    public static IServiceCollection AddShateM(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ShateMOptions>(configuration.GetSection(ShateMOptions.Section));
        services.AddSingleton<ShateMTokenCache>();
        services.AddProviderHttpClient<ShateMClient, ShateMClient>($"{ShateMOptions.Section}:BaseUrl");
        services.AddSingleton<IQuotationProvider, ShateMProvider>();
        return services;
    }
}
