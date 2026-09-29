using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;

namespace SupplierQuotationApi.Providers.Mikado;

public sealed class MikadoProvider : IQuotationProvider
{
    private readonly IServiceProvider _services;
    private readonly MikadoOptions _options;
    private readonly IProducerAliasService _aliases;

    public MikadoProvider(IServiceProvider services, IOptions<MikadoOptions> options, IProducerAliasService aliases)
    {
        _services = services;
        _options = options.Value;
        _aliases = aliases;
    }

    public string Key => "MikadoMskHod20";
    public string Name => "Микадо (МскХод20)";
    // Логотип лежит у самого поставщика: отдаём ссылку, файл в репозиторий не копируем.
    public string? LogoFile => "https://mikado-parts.ru/img/MikadoLogo.svg";
    public string? AccountLogin => _options.ClientId;
    public bool IsEnabled => _options.IsConfigured;

    public async Task<IReadOnlyList<QuotationOffer>> SearchAsync(QuotationSearch search, CancellationToken cancellationToken)
    {
        // Микадо ищет только по паре код + бренд: без бренда API не вызывается.
        if (string.IsNullOrWhiteSpace(search.Brand))
            throw new MikadoApiException("Микадо ищет только по паре артикул + бренд: укажите бренд.");

        // Клиент транзитный: свежий из фабрики, чтобы работала ротация обработчиков.
        var client = _services.GetRequiredService<MikadoClient>();
        var aliases = await _aliases.GetMapAsync(cancellationToken);

        // Бренд у Микадо нестрогий по регистру, но не по названию: пробуем исходное написание и алиасы.
        var responses = await Task.WhenAll(aliases.QueryVariants(search.Brand, 3)
            .Select(b => client.CodeBrandStockInfoAsync(search.Article, b, cancellationToken)));

        return MikadoMapper.Map(responses.SelectMany(x => x), search.Article)
            .OrderBy(x => x.Price.Amount)
            .ToList();
    }
}
