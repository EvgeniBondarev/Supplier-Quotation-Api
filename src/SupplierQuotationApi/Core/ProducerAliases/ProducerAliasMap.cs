using System.Text;

namespace SupplierQuotationApi.Core.ProducerAliases;

/// <summary>
/// Алиасы производителей из таблицы OneCProducerAliases базы Studio2: «KAYABA» → «KYB».
/// Сравнивает бренд запроса и бренд ответа поставщика, не завися от написания.
/// </summary>
public sealed class ProducerAliasMap
{
    /// <summary>Пустая карта: сравнение только по нормализации и вхождению (БД Studio2 недоступна или не настроена).</summary>
    public static readonly ProducerAliasMap Empty = new(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> _aliasToCanonical;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _canonicalToVariants;

    public ProducerAliasMap(IReadOnlyDictionary<string, string> aliasToCanonical)
    {
        _aliasToCanonical = aliasToCanonical;
        _canonicalToVariants = aliasToCanonical
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(pair => pair.Key).Append(group.Key).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
    }

    public int Count => _aliasToCanonical.Count;

    /// <summary>Нормализованное каноническое написание. Незнакомый бренд остаётся собой.</summary>
    public string Canonicalize(string? brand)
    {
        var normalized = Normalize(brand);
        return _aliasToCanonical.TryGetValue(normalized, out var canonical) ? canonical : normalized;
    }

    /// <summary>Один и тот же бренд с точностью до написания и алиаса.</summary>
    public bool AreSame(string? left, string? right) =>
        Canonicalize(left) is { Length: > 0 } canonical && canonical == Canonicalize(right);

    /// <summary>Все известные написания в нормализованном виде: «Kayaba» → [«KAYABA», «KYB»]. Для незнакомого — он сам.</summary>
    public IReadOnlyList<string> GetVariants(string? brand)
    {
        var canonical = Canonicalize(brand);
        if (canonical.Length == 0) return [];
        return _canonicalToVariants.TryGetValue(canonical, out var variants) ? variants : [canonical];
    }

    /// <summary>Бренд ответа подходит запрошенному: пустой запрос — всё; иначе совпадение по нормализации, алиасу или вхождению
    /// («MAHLE» ↔ «MAHLE ORIGINAL»). Вхождение проверяется по всем известным написаниям запрошенного бренда.</summary>
    public bool Matches(string? actual, string? requested)
    {
        var wanted = Normalize(requested);
        if (wanted.Length == 0) return true;

        var brand = Normalize(actual);
        if (brand.Length == 0) return false;
        if (brand == wanted || AreSame(brand, wanted)) return true;

        foreach (var variant in GetVariants(wanted))
            if (brand.Contains(variant, StringComparison.Ordinal) || variant.Contains(brand, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>Написания бренда для запроса к API, которое ищет только по точному имени: исходное, очищенное от знаков
    /// («WYNN'S» → «WYNNS») и известные алиасы («Kayaba» → «KYB»). Без повторов, не больше <paramref name="max"/>.</summary>
    public IReadOnlyList<string> QueryVariants(string? brand, int max = 4)
    {
        var original = (brand ?? string.Empty).Trim();
        if (original.Length == 0) return [];

        var result = new List<string> { original };
        void Add(string candidate)
        {
            if (candidate.Length > 0 && result.Count < max && !result.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                result.Add(candidate);
        }

        Add(new string(original.Where(char.IsLetterOrDigit).ToArray()));
        foreach (var variant in GetVariants(original)) Add(variant);
        return result;
    }

    /// <summary>Нормализация повторяет ту, которой заполнялась таблица алиасов в Studio2 (FormKC, верхний регистр, буквы и цифры).</summary>
    public static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return new string(normalized.Where(char.IsLetterOrDigit).ToArray());
    }

    /// <summary>Строит карту: цепочки A→B→C схлопываются заранее, лимит шагов защищает от кольца, заведённого руками.</summary>
    public static ProducerAliasMap Build(IEnumerable<(string AliasNorm, string? CanonicalName)> rows)
    {
        var direct = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (aliasNorm, canonicalName) in rows)
        {
            var alias = Normalize(aliasNorm);
            var canonical = Normalize(canonicalName);
            if (alias.Length == 0 || canonical.Length == 0 || alias == canonical) continue;
            direct[alias] = canonical;
        }

        return new ProducerAliasMap(direct.ToDictionary(pair => pair.Key, pair =>
        {
            var canonical = pair.Value;
            for (var step = 0; step < 5 && direct.TryGetValue(canonical, out var next) && next != canonical; step++)
                canonical = next;
            return canonical;
        }, StringComparer.Ordinal));
    }
}
