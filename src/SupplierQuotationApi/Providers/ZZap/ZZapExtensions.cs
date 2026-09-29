using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.ZZap;

public static class ZZapExtensions
{
    public static IServiceCollection AddZZap(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ZZapOptions>(configuration.GetSection(ZZapOptions.Section));
        // Троттлинг общий на процесс: ZZap считает частоту по ключу, а не по соединению.
        services.AddSingleton<ZZapThrottle>();
        services.AddProviderHttpClient<ZZapClient, ZZapClient>($"{ZZapOptions.Section}:BaseUrl");
        services.AddSingleton<IQuotationProvider, ZZapProvider>();
        return services;
    }
}
