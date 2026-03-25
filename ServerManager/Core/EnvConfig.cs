using System.Text;
using System.Text.RegularExpressions;

namespace TacidManager.Core;

// ─── Модель чтения/записи env-файла ─────────────────────────────────────────

/// <summary>
/// Типизированная модель env-файла сервера T.A.C.I.D.
/// Сохраняет порядок строк, комментарии и форматирование при round-trip.
/// </summary>
internal sealed class EnvConfig
{
    private readonly List<RawLine> _lines = new();

    // ═══ Криптографические ключи ══════════════════════════════════════════

    /// <summary>JWT Signing Key (HMAC-SHA256). Минимум 32 байта UTF-8.</summary>
    public string? JwtSigningKey
    {
        get => Get("TACID_JWT_SIGNING_KEY");
        set => Set("TACID_JWT_SIGNING_KEY", value);
    }

    /// <summary>Master Key для AES-256-GCM шифрования ключей маяков. Ровно 32 байта → Base64.</summary>
    public string? MasterKeyB64
    {
        get => Get("TACID_MASTER_KEY_B64");
        set => Set("TACID_MASTER_KEY_B64", value);
    }

    /// <summary>Разрешить HTTP без TLS (для LAN / WireGuard VPN).</summary>
    public bool AllowInsecureHttp
    {
        get => string.Equals(Get("TACID_ALLOW_INSECURE_HTTP"), "true", StringComparison.OrdinalIgnoreCase);
        set => Set("TACID_ALLOW_INSECURE_HTTP", value ? "true" : "false");
    }

    // ═══ Учётные данные ═══════════════════════════════════════════════════

    public string? AdminLogin         { get => Get("TACID_ADMIN_LOGIN");          set => Set("TACID_ADMIN_LOGIN", value); }
    public string? AdminPassword      { get => Get("TACID_ADMIN_PASSWORD");       set => Set("TACID_ADMIN_PASSWORD", value); }
    public string? ObserverLogin      { get => Get("TACID_OBSERVER_LOGIN");       set => Set("TACID_OBSERVER_LOGIN", value); }
    public string? ObserverPassword   { get => Get("TACID_OBSERVER_PASSWORD");    set => Set("TACID_OBSERVER_PASSWORD", value); }
    public string? PlayerLogin        { get => Get("TACID_PLAYER_LOGIN");         set => Set("TACID_PLAYER_LOGIN", value); }
    public string? PlayerPassword     { get => Get("TACID_PLAYER_PASSWORD");      set => Set("TACID_PLAYER_PASSWORD", value); }
    public string? PlayerBeaconId     { get => Get("TACID_PLAYER_BEACON_ID");     set => Set("TACID_PLAYER_BEACON_ID", value); }

    // ═══ Среда выполнения / URL ══════════════════════════════════════════

    public string? AspNetCoreEnvironment { get => Get("ASPNETCORE_ENVIRONMENT"); set => Set("ASPNETCORE_ENVIRONMENT", value); }
    public string? AspNetCoreUrls        { get => Get("ASPNETCORE_URLS");        set => Set("ASPNETCORE_URLS", value); }

    // ═══ База данных / Redis ═════════════════════════════════════════════

    public string? PostgresConnectionString { get => Get("ConnectionStrings__PostgreSQL"); set => Set("ConnectionStrings__PostgreSQL", value); }
    public string? RedisConnectionString    { get => Get("Redis__ConnectionString");       set => Set("Redis__ConnectionString", value); }

    // ─── Публичный доступ к произвольным ключам (для SecurityMenu) ───────

    internal string? Get(string key)
        => _lines.LastOrDefault(l => l.Key == key)?.Value;

    internal void Set(string key, string? value)
    {
        if (value is null) return;
        var idx = _lines.FindLastIndex(l => l.Key == key);
        var raw = $"{key}={value}";
        if (idx >= 0)
            _lines[idx] = new RawLine(raw, key, value);
        else
            _lines.Add(new RawLine(raw, key, value));
    }

    internal void Remove(string key)
    {
        _lines.RemoveAll(l => l.Key == key);
    }

    // ─── Загрузка / Сохранение ────────────────────────────────────────────

    public static EnvConfig Load(string path)
    {
        var cfg = new EnvConfig();
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            cfg._lines.Add(ParseLine(line));
        return cfg;
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllLines(path, _lines.Select(l => l.Raw), Encoding.UTF8);
    }

    // ─── Шаблон новой конфигурации ────────────────────────────────────────

    public static EnvConfig CreateTemplate()
    {
        var cfg = new EnvConfig();
        cfg._lines.AddRange(
        [
            Comment("# ════════════════════════════════════════════════════════════"),
            Comment("# T.A.C.I.D. Server Environment — сгенерировано tacid-manager"),
            Comment("# Права файла: chmod 640 / chown root:strikeball"),
            Comment("# ════════════════════════════════════════════════════════════"),
            Blank(),
            Comment("# ─── Криптографические ключи ───────────────────────────────"),
            Kv("TACID_JWT_SIGNING_KEY", ""),
            Kv("TACID_MASTER_KEY_B64", ""),
            Blank(),
            Comment("# ─── Учётные данные ────────────────────────────────────────"),
            Kv("TACID_ADMIN_LOGIN", ""),
            Kv("TACID_ADMIN_PASSWORD", ""),
            Kv("TACID_OBSERVER_LOGIN", ""),
            Kv("TACID_OBSERVER_PASSWORD", ""),
            Kv("TACID_PLAYER_LOGIN", ""),
            Kv("TACID_PLAYER_PASSWORD", ""),
            Kv("TACID_PLAYER_BEACON_ID", "1"),
            Blank(),
            Comment("# ─── Режим подключения ──────────────────────────────────────"),
            Kv("ASPNETCORE_ENVIRONMENT", "Production"),
            Kv("ASPNETCORE_URLS", "http://0.0.0.0:5001"),
            Kv("TACID_ALLOW_INSECURE_HTTP", "false"),
            Blank(),
            Comment("# ─── База данных ─────────────────────────────────────────────"),
            Kv("ConnectionStrings__PostgreSQL",
               "Host=localhost;Database=strikeballdb;Username=strikeballuser;Password=changeme"),
            Blank(),
            Comment("# ─── Redis ───────────────────────────────────────────────────"),
            Kv("Redis__ConnectionString", "localhost:6379,abortConnect=false"),
        ]);
        return cfg;
    }

    // ─── Валидация ────────────────────────────────────────────────────────

    public List<ConfigIssue> Validate()
    {
        var issues = new List<ConfigIssue>();

        RequireKey(issues, "TACID_JWT_SIGNING_KEY", JwtSigningKey,
            v => CryptoUtils.IsValidJwtKey(v),
            "Минимум 32 байта UTF-8. Генерируйте через меню «Ключи»");

        RequireKey(issues, "TACID_MASTER_KEY_B64", MasterKeyB64,
            v => CryptoUtils.IsValidMasterKey(v),
            "Должен декодироваться в ровно 32 байта (AES-256-GCM)");

        if (string.IsNullOrWhiteSpace(AdminLogin) || string.IsNullOrWhiteSpace(AdminPassword))
            issues.Add(new ConfigIssue(Severity.Warning, "TACID_ADMIN_*",
                "Администратор не задан — доступ к API /api/security, /api/beacons, /api/anchors невозможен"));
        else if (AdminPassword!.Length < 12)
            issues.Add(new ConfigIssue(Severity.Warning, "TACID_ADMIN_PASSWORD",
                $"Пароль слишком короткий ({AdminPassword.Length} симв.) — рекомендуется ≥ 12"));

        if (string.IsNullOrWhiteSpace(ObserverLogin))
            issues.Add(new ConfigIssue(Severity.Info, "TACID_OBSERVER_LOGIN",
                "Роль observer не задана — клиент (Android КПК) не сможет войти как наблюдатель"));

        var env = AspNetCoreEnvironment ?? "Production";
        var isProduction = string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase);

        if (isProduction && string.IsNullOrWhiteSpace(PostgresConnectionString))
            issues.Add(new ConfigIssue(Severity.Error, "ConnectionStrings__PostgreSQL",
                "Production-режим требует PostgreSQL. Задайте строку подключения"));

        if (string.IsNullOrWhiteSpace(RedisConnectionString))
            issues.Add(new ConfigIssue(Severity.Warning, "Redis__ConnectionString",
                "Redis не настроен — replay protection, rate limiting и JWT denylist недоступны"));

        if (AllowInsecureHttp)
            issues.Add(new ConfigIssue(Severity.Warning, "TACID_ALLOW_INSECURE_HTTP",
                "HTTP-режим активен — JWT-токены передаются открытым текстом. Только LAN/VPN!"));

        if (!string.IsNullOrWhiteSpace(PlayerLogin) && string.IsNullOrWhiteSpace(PlayerBeaconId))
            issues.Add(new ConfigIssue(Severity.Warning, "TACID_PLAYER_BEACON_ID",
                "Player создан без привязки к маяку — телеметрия не будет ассоциирована с игроком"));

        return issues;
    }

    // ─── Экспорт ──────────────────────────────────────────────────────────

    public string ExportText()
        => string.Join(Environment.NewLine, _lines.Select(l => l.Raw));

    // ─── Внутренние ───────────────────────────────────────────────────────

    private static RawLine ParseLine(string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            return new RawLine(raw, null, null);

        var eq = trimmed.IndexOf('=');
        if (eq <= 0)
            return new RawLine(raw, null, null);

        var key = trimmed[..eq].Trim();
        var val = trimmed[(eq + 1)..].Trim().Trim('"').Trim('\'');
        return new RawLine(raw, key, val);
    }

    private static void RequireKey(List<ConfigIssue> issues, string key, string? value,
        Func<string, bool> validator, string hint)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(new ConfigIssue(Severity.Error, key, $"Не задан — {hint}"));
        else if (!validator(value))
            issues.Add(new ConfigIssue(Severity.Error, key, $"Некорректное значение — {hint}"));
    }

    private static RawLine Comment(string text) => new(text, null, null);
    private static RawLine Blank()               => new(string.Empty, null, null);
    private static RawLine Kv(string k, string v) => new($"{k}={v}", k, v);
}

// ─── Вспомогательные типы ────────────────────────────────────────────────────

internal sealed record RawLine(string Raw, string? Key, string? Value);

internal sealed record ConfigIssue(Severity Level, string Key, string Message);

internal enum Severity { Info, Warning, Error }
