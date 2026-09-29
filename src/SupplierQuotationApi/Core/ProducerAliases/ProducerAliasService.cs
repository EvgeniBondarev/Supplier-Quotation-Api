using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace SupplierQuotationApi.Core.ProducerAliases;

/// <summary>Подключение к базе Studio2: SUPPLIERS не нужны, только чтение алиасов производителей.</summary>
public sealed class Studio2DbOptions
{
    public const string Section = "Studio2Db";

    /// <summary>Строка подключения к MariaDB Studio2 (схема ST2). Пусто — алиасы отключены.</summary>
    public string? ConnectionString { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}

public interface IProducerAliasService
{
    Task<ProducerAliasMap> GetMapAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Читает таблицу OneCProducerAliases базы Studio2 (десятки строк, правится вручную) и держит её в кэше.
/// Только чтение: сессия открывается в режиме READ ONLY, чужие данные сервис изменить не может.
/// Недоступная БД не ломает проценку: карта пустая, повторная попытка через две минуты.
/// </summary>
public sealed class ProducerAliasService : IProducerAliasService
{
    private const string CacheKey = "producer-aliases";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromMinutes(2);

    private readonly Studio2DbOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ProducerAliasService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public ProducerAliasService(IOptions<Studio2DbOptions> options, IMemoryCache cache, ILogger<ProducerAliasService> logger)
    {
        _options = options.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ProducerAliasMap> GetMapAsync(CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured) return ProducerAliasMap.Empty;
        if (_cache.TryGetValue(CacheKey, out ProducerAliasMap? cached) && cached is not null) return cached;

        // Один загрузчик на все параллельные проценки: остальные ждут и берут результат из кэша.
        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(CacheKey, out cached) && cached is not null) return cached;

            try
            {
                var map = ProducerAliasMap.Build(await ReadRowsAsync(cancellationToken));
                _logger.LogInformation("Загружено алиасов производителей из Studio2: {Count}.", map.Count);
                _cache.Set(CacheKey, map, Ttl);
                return map;
            }
            catch (Exception ex) when (ex is MySqlException or TimeoutException or InvalidOperationException or IOException)
            {
                // Текст ошибки MySqlConnector может содержать хост, но не пароль; строку подключения не логируем.
                _logger.LogWarning("База Studio2 недоступна, проценка идёт без алиасов производителей: {Message}{Inner}",
                    ex.Message, ex.InnerException is null ? string.Empty : $" ({ex.InnerException.Message})");
                _cache.Set(CacheKey, ProducerAliasMap.Empty, FailureTtl);
                return ProducerAliasMap.Empty;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<List<(string, string?)>> ReadRowsAsync(CancellationToken ct)
    {
        await using var connection = new MySqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        await using (var readOnly = new MySqlCommand("SET SESSION TRANSACTION READ ONLY", connection))
            await readOnly.ExecuteNonQueryAsync(ct);

        await using var command = new MySqlCommand("SELECT AliasNorm, CanonicalName FROM OneCProducerAliases", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = new List<(string, string?)>();
        while (await reader.ReadAsync(ct))
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        return rows;
    }
}
