namespace SupplierQuotationApi.Providers.ForumAuto;

/// <summary>Один аккаунт Forum-Auto: SUPPLIERS__FORUMAUTO__*, ...INTERPARTS__*, ...PITER__*, ...ROSTOV__*, ...ISTRA__*.</summary>
public sealed class ForumAutoOptions
{
    public string? BaseUrl { get; set; } = "https://api.forum-auto.ru/v2";
    public string? Login { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrWhiteSpace(Password);
}

public sealed record ForumAutoAccount(string Key, string Name, string ConfigSection);
