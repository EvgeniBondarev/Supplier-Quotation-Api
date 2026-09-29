using SupplierQuotationApi.Core.ProducerAliases;
using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.FavoritParts;

public static class FavoritPartsExtensions
{
    private static readonly FavoritPartsAccount[] Accounts =
    [
        new("FavoritParts", "Фаворит Союз", "favorit-parts.png", "Suppliers:FavoritParts", "FavoritParts"),
        new("FavoritPartsIstra", "Фаворит Истра", "favorit-parts.png", "Suppliers:FavoritPartsIstra", "FavoritPartsIstra"),
        new("FavoritPartsRostov", "Фаворит Ростов", "favorit-parts.png", "Suppliers:FavoritPartsRostov", "FavoritPartsRostov")
    ];

    /// <summary>Регистрирует три аккаунта FavoritParts: одна реализация, по экземпляру на аккаунт.</summary>
    public static IServiceCollection AddFavoritParts(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (var account in Accounts)
        {
            var options = configuration.GetSection(account.ConfigSection).Get<FavoritPartsOptions>() ?? new FavoritPartsOptions();
            if (string.IsNullOrWhiteSpace(options.BaseUrl)) options.BaseUrl = "https://api.favorit-parts.ru";

            services.AddProviderHttpClient(account.HttpClientName, $"{account.ConfigSection}:BaseUrl");
            services.AddSingleton<IQuotationProvider>(sp => new FavoritPartsProvider(
                account, options,
                new FavoritPartsClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(account.HttpClientName), options),
                sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IProducerAliasService>()));
        }
        return services;
    }
}
