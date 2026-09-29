using SupplierQuotationApi.Infrastructure;

namespace SupplierQuotationApi.Providers.ForumAuto;

public static class ForumAutoExtensions
{
    private static readonly ForumAutoAccount[] Accounts =
    [
        new("ForumAuto", "Форум-Авто (Союз)", "Suppliers:ForumAuto"),
        new("ForumAutoInterparts", "Форум-Авто (Интерпартс)", "Suppliers:ForumAutoInterparts"),
        new("ForumAutoPiter", "Форум-Авто (Питер)", "Suppliers:ForumAutoPiter"),
        new("ForumAutoRostov", "Форум-Авто (Ростов)", "Suppliers:ForumAutoRostov"),
        new("ForumAutoIstra", "Форум-Авто (Истра)", "Suppliers:ForumAutoIstra")
    ];

    /// <summary>Регистрирует пять аккаунтов Forum-Auto: одна реализация, по экземпляру на аккаунт.</summary>
    public static IServiceCollection AddForumAuto(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (var account in Accounts)
        {
            var options = configuration.GetSection(account.ConfigSection).Get<ForumAutoOptions>() ?? new ForumAutoOptions();
            if (string.IsNullOrWhiteSpace(options.BaseUrl)) options.BaseUrl = "https://api.forum-auto.ru/v2";

            services.AddProviderHttpClient(account.Key, $"{account.ConfigSection}:BaseUrl");
            services.AddSingleton<IQuotationProvider>(sp => new ForumAutoProvider(
                account, options,
                new ForumAutoClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient(account.Key), options)));
        }
        return services;
    }
}
