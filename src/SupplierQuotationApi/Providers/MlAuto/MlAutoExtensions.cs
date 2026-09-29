using Microsoft.Extensions.Caching.Memory;
using SupplierQuotationApi.Core;
using SupplierQuotationApi.Core.ProducerAliases;
using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.MlAuto;

public static class MlAutoExtensions
{
    // Логотип ML-Auto лежит у самого поставщика: отдаём ссылку, файл в репозиторий не копируем.
    private const string Logo = "https://www.ml-auto.by/media/files/settings/inside-placeholder-logo-mobile.png";

    private static readonly MlAutoAccount[] Accounts =
    [
        new("MlAuto", "ML-Auto BY", "Минск", "BYN", "Suppliers:MlAuto", "https://www.ml-auto.by/webservice", Logo),
        new("MlAutoRu", "ML-Auto RU", "Россия", "RUB", "Suppliers:MlAutoRu", "https://ml-auto.ru/webservice", Logo)
    ];

    /// <summary>Регистрирует контуры ML-Auto (Беларусь и Россия): одна реализация, по экземпляру на контур.</summary>
    public static IServiceCollection AddMlAuto(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (var account in Accounts)
        {
            var options = configuration.GetSection(account.ConfigSection).Get<MlAutoOptions>() ?? new MlAutoOptions();
            if (string.IsNullOrWhiteSpace(options.BaseUrl)) options.BaseUrl = account.DefaultBaseUrl;

            services.AddProviderHttpClient(account.Key, $"{account.ConfigSection}:BaseUrl");
            services.AddSingleton<IQuotationProvider>(sp => new MlAutoProvider(
                account, options,
                new MlAutoClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(account.Key), options),
                sp.GetRequiredService<ICurrencyConverter>(), sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IProducerAliasService>()));
        }
        return services;
    }
}
