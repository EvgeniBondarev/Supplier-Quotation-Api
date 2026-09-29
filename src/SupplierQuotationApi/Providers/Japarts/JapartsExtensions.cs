using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.Japarts;

public static class JapartsExtensions
{
    public static IServiceCollection AddJaparts(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JapartsOptions>(configuration.GetSection(JapartsOptions.Section));
        services.AddProviderHttpClient<JapartsClient, JapartsClient>($"{JapartsOptions.Section}:BaseUrl");
        services.AddSingleton<IQuotationProvider, JapartsProvider>();
        return services;
    }
}
