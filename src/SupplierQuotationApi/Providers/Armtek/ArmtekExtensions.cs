using Microsoft.Extensions.Caching.Memory;
using SupplierQuotationApi.Core;
using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.Armtek;

public static class ArmtekExtensions
{
    private static readonly ArmtekAccount[] Accounts =
    [
        new("Armtek", "Армтек", "armtek.svg", "Suppliers:Armtek", "Armtek"),
        new("ArmtekBy", "Армтек BY", "armtek.svg", "Suppliers:ArmtekBy", "ArmtekBy")
    ];

    /// <summary>Регистрирует аккаунты Armtek (RU и BY): одна реализация, по экземпляру на аккаунт.</summary>
    public static IServiceCollection AddArmtek(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (var account in Accounts)
        {
            var options = configuration.GetSection(account.ConfigSection).Get<ArmtekOptions>() ?? new ArmtekOptions();
            if (string.IsNullOrWhiteSpace(options.BaseUrl)) options.BaseUrl = "https://ws.armtek.ru";

            services.AddProviderHttpClient(account.HttpClientName, $"{account.ConfigSection}:BaseUrl");
            services.AddSingleton<IQuotationProvider>(sp => new ArmtekProvider(
                account, options,
                new ArmtekClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(account.HttpClientName), options,
                    sp.GetRequiredService<TimeProvider>()),
                sp.GetRequiredService<ICurrencyConverter>(), sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<TimeProvider>()));
        }
        return services;
    }
}
