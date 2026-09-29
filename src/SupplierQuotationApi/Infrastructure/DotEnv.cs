namespace SupplierQuotationApi.Infrastructure;

/// <summary>Загружает .env в переменные окружения процесса. Уже заданные переменные не перезаписываются.</summary>
public static class DotEnv
{
    public static void Load(string fileName = ".env")
    {
        var path = FindFile(fileName);
        if (path is null) return;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var name = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());
            if (Environment.GetEnvironmentVariable(name) is null)
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '\'' || value[0] == '"') && value[^1] == value[0]
            ? value[1..^1]
            : value;

    private static string? FindFile(string fileName)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}
