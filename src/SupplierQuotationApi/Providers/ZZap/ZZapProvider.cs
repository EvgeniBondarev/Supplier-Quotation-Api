using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;

namespace SupplierQuotationApi.Providers.ZZap;

public sealed class ZZapProvider : IQuotationProvider
{
    private readonly IServiceProvider _services;
    private readonly ZZapOptions _options;

    public ZZapProvider(IServiceProvider services, IOptions<ZZapOptions> options)
    {
        _services = services;
        _options = options.Value;
    }

    public string Key => "ZZapMoscow";
    public string Name => "ZZap Москва";
    public string? LogoFile => "zzap.svg";
    /// <summary>Аккаунт задаёт ключ API: сам ключ не раскрываем.</summary>
    public string? AccountLogin => "api-key";
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Без бренда API отдаёт пустой список, а каждый лишний запрос стоит слота в общей очереди (1 запрос в 3,5 с).
        if (string.IsNullOrWhiteSpace(search.Brand))
            throw new ZZapApiException("ZZap ищет только по паре артикул + бренд: укажите бренд.");

        // Клиент транзитный: свежий из фабрики, чтобы работала ротация обработчиков.
        var client = _services.GetRequiredService<ZZapClient>();

        // Одна попытка с исходным брендом: ZZap сам приводит написания («Kayaba» = «KYB», «WYNN'S» = «WYNNS»),
        // а несколько вариантов упёрлись бы в лимит частоты.
        var offers = await client.SearchLightAsync(search.Article, search.Brand, cancellationToken);

        return ZZapMapper.MapOriginals(offers, search.Article, _options.CodeRegion);
    }
}
