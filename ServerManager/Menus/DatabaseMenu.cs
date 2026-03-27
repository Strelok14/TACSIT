using System.Text.RegularExpressions;
using System.Net.Sockets;
using TacidManager.Core;
using TacidManager.UI;

namespace TacidManager.Menus;

internal static class DatabaseMenu
{
    public static Task Run(EnvConfig config)
    {
        while (true)
        {
            ConsoleUI.Clear();
            ConsoleUI.PrintBanner();
            ConsoleUI.Header("База данных и Redis");

            ConsoleUI.StatusRow("PostgreSQL",
                MaskPassword(config.PostgresConnectionString));
            ConsoleUI.StatusRow("Redis",
                config.RedisConnectionString);
            Console.WriteLine();

            ConsoleUI.Info("PostgreSQL — используется в Production (ASPNETCORE_ENVIRONMENT=Production).");
            ConsoleUI.Info("Redis      — хранит:");
            ConsoleUI.Info("             • sequence numbers маяков (защита от replay-атак)");
            ConsoleUI.Info("             • счётчики rate limiting");
            ConsoleUI.Info("             • JTI отозванных JWT-токенов (denylist при logout)");
            ConsoleUI.Warning("Development-режим использует SQLite (strikeball.db) — PostgreSQL не нужен.");
            Console.WriteLine();

            ConsoleUI.Header("Действия");
            ConsoleUI.MenuItem(1, "Настроить PostgreSQL");
            ConsoleUI.MenuItem(2, "Настроить Redis");
            ConsoleUI.MenuItem(3, "Показать строки подключения (открытый текст)");
            ConsoleUI.MenuItem(4, "Redis мастер настройки (пошагово)");
            ConsoleUI.MenuItem(5, "Redis пресеты (LAN / Docker / TLS)");
            ConsoleUI.MenuItem(6, "Проверить доступность Redis endpoint");
            ConsoleUI.MenuItem(0, "Назад");

            var choice = ConsoleUI.SelectOption("Выберите", 6);
            switch (choice)
            {
                case 1: EditPostgres(config); break;
                case 2: EditRedis(config);    break;
                case 3: ShowRaw(config);      break;
                case 4: RedisWizard(config);  break;
                case 5: RedisPresets(config); break;
                case 6: ProbeRedisEndpoint(config); break;
                case 0: return Task.CompletedTask;
            }
        }
    }

    // ─── PostgreSQL ────────────────────────────────────────────────────────

    private static void EditPostgres(EnvConfig config)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("PostgreSQL — строка подключения");
        ConsoleUI.Info("Переменная: ConnectionStrings__PostgreSQL");
        Console.WriteLine();
        ConsoleUI.Info("Формат Npgsql:");
        ConsoleUI.Info("  Host=localhost;Port=5432;Database=strikeballdb;Username=strikeball;Password=secret");
        ConsoleUI.Info("С TLS:");
        ConsoleUI.Info("  ...;SslMode=Require;TrustServerCertificate=true");
        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(config.PostgresConnectionString))
        {
            ConsoleUI.Info($"Текущая (masked): {MaskPassword(config.PostgresConnectionString)}");
            Console.WriteLine();
        }

        var val = ConsoleUI.Prompt("Строка подключения (Enter = без изменений)");
        if (!string.IsNullOrWhiteSpace(val))
        {
            config.PostgresConnectionString = val.Trim();
            ConsoleUI.Success("PostgreSQL строка обновлена.");
        }
        else ConsoleUI.Info("Без изменений.");

        ConsoleUI.PressAnyKey();
    }

    // ─── Redis ────────────────────────────────────────────────────────────

    private static void EditRedis(EnvConfig config)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("Redis — строка подключения");
        ConsoleUI.Info("Переменная: Redis__ConnectionString");
        Console.WriteLine();
        ConsoleUI.Info("Формат StackExchange.Redis:");
        ConsoleUI.Info("  localhost:6379,abortConnect=false");
        ConsoleUI.Info("  myredis.example.com:6380,password=secret,ssl=true,abortConnect=false");
        ConsoleUI.Info("  127.0.0.1:6379,connectTimeout=3000,syncTimeout=1000");
        Console.WriteLine();

        var val = ConsoleUI.Prompt("Redis строка (Enter = без изменений)", config.RedisConnectionString);
        if (!string.IsNullOrWhiteSpace(val))
        {
            config.RedisConnectionString = val.Trim();
            ConsoleUI.Success("Redis строка обновлена.");
        }
        else ConsoleUI.Info("Без изменений.");

        ConsoleUI.PressAnyKey();
    }

    private static void RedisWizard(EnvConfig config)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("Redis мастер настройки");

        var host = ConsoleUI.Prompt("Host", "localhost")?.Trim();
        if (string.IsNullOrWhiteSpace(host)) host = "localhost";

        var portRaw = ConsoleUI.Prompt("Port", "6379")?.Trim();
        if (!int.TryParse(portRaw, out var port) || port <= 0 || port > 65535)
        {
            ConsoleUI.Error("Некорректный port. Использую 6379.");
            port = 6379;
        }

        var useTls = ConsoleUI.Confirm("Включить TLS (ssl=true)?", defaultYes: false);
        var usePassword = ConsoleUI.Confirm("Требуется пароль Redis?", defaultYes: false);
        string? password = null;
        if (usePassword)
        {
            password = ConsoleUI.PromptPassword("Redis password");
            if (string.IsNullOrWhiteSpace(password))
            {
                ConsoleUI.Warning("Пароль не введён — поле password не будет добавлено.");
                usePassword = false;
            }
        }

        var dbRaw = ConsoleUI.Prompt("DB index (0..15)", "0")?.Trim();
        if (!int.TryParse(dbRaw, out var db) || db < 0)
        {
            db = 0;
        }

        var parts = new List<string>
        {
            $"{host}:{port}",
            "abortConnect=false",
            "connectTimeout=3000",
            "syncTimeout=1000",
            $"defaultDatabase={db}"
        };

        if (useTls)
        {
            parts.Add("ssl=true");
        }
        if (usePassword)
        {
            parts.Add($"password={password}");
        }

        var value = string.Join(',', parts);
        config.RedisConnectionString = value;
        ConsoleUI.Success("Redis строка сформирована и сохранена в конфиге.");
        ConsoleUI.Info($"Redis__ConnectionString = {MaskPassword(value)}");
        ConsoleUI.PressAnyKey();
    }

    private static void RedisPresets(EnvConfig config)
    {
        while (true)
        {
            ConsoleUI.Clear();
            ConsoleUI.PrintBanner();
            ConsoleUI.Header("Redis пресеты");
            ConsoleUI.MenuItem(1, "LAN / localhost (рекомендуется для сервера)");
            ConsoleUI.MenuItem(2, "Docker compose (redis:6379)");
            ConsoleUI.MenuItem(3, "Удалённый Redis + TLS (ввести host/port/password)");
            ConsoleUI.MenuItem(0, "Назад");

            var choice = ConsoleUI.SelectOption("Выберите", 3);
            switch (choice)
            {
                case 1:
                    config.RedisConnectionString = "localhost:6379,abortConnect=false,connectTimeout=3000,syncTimeout=1000";
                    ConsoleUI.Success("Применён пресет localhost.");
                    ConsoleUI.PressAnyKey();
                    break;
                case 2:
                    config.RedisConnectionString = "redis:6379,abortConnect=false,connectTimeout=3000,syncTimeout=1000";
                    ConsoleUI.Success("Применён пресет Docker (redis:6379).");
                    ConsoleUI.PressAnyKey();
                    break;
                case 3:
                    ApplyRemoteTlsPreset(config);
                    break;
                case 0:
                    return;
            }
        }
    }

    private static void ApplyRemoteTlsPreset(EnvConfig config)
    {
        var host = ConsoleUI.Prompt("Redis host", "myredis.example.com")?.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            ConsoleUI.Error("Host обязателен.");
            ConsoleUI.PressAnyKey();
            return;
        }

        var portRaw = ConsoleUI.Prompt("Port", "6380")?.Trim();
        if (!int.TryParse(portRaw, out var port) || port <= 0 || port > 65535)
        {
            port = 6380;
        }

        var password = ConsoleUI.PromptPassword("Redis password");
        if (string.IsNullOrWhiteSpace(password))
        {
            ConsoleUI.Error("Пароль обязателен для TLS preset.");
            ConsoleUI.PressAnyKey();
            return;
        }

        config.RedisConnectionString = $"{host}:{port},ssl=true,password={password},abortConnect=false,connectTimeout=5000,syncTimeout=2000";
        ConsoleUI.Success("Применён пресет удалённого Redis + TLS.");
        ConsoleUI.PressAnyKey();
    }

    private static void ProbeRedisEndpoint(EnvConfig config)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("Проверка доступности Redis endpoint");

        var conn = config.RedisConnectionString;
        if (string.IsNullOrWhiteSpace(conn))
        {
            ConsoleUI.Error("Redis__ConnectionString не задан.");
            ConsoleUI.PressAnyKey();
            return;
        }

        if (!TryExtractEndpoint(conn, out var host, out var port))
        {
            ConsoleUI.Error("Не удалось извлечь endpoint из Redis строки.");
            ConsoleUI.Info("Ожидается формат типа host:port,...");
            ConsoleUI.PressAnyKey();
            return;
        }

        ConsoleUI.Info($"Пробую TCP connect: {host}:{port}");
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = connectTask.Wait(TimeSpan.FromSeconds(3));
            if (!completed || !client.Connected)
            {
                ConsoleUI.Error("Endpoint недоступен (timeout). Проверьте host/port и firewall.");
            }
            else
            {
                ConsoleUI.Success("Endpoint доступен по TCP.");
                ConsoleUI.Info("Это базовая проверка сети. Авторизация Redis отдельно не проверяется.");
            }
        }
        catch (Exception ex)
        {
            ConsoleUI.Error($"Ошибка подключения: {ex.Message}");
        }

        ConsoleUI.PressAnyKey();
    }

    // ─── Показать в открытом виде ─────────────────────────────────────────

    private static void ShowRaw(EnvConfig config)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("Строки подключения (открытый текст)");
        ConsoleUI.Warning("Убедитесь, что экран не доступен посторонним!");
        Console.WriteLine();

        if (!ConsoleUI.Confirm("Показать пароли открытым текстом?", defaultYes: false))
        {
            ConsoleUI.Info("Отменено.");
            ConsoleUI.PressAnyKey();
            return;
        }

        Console.WriteLine();
        var prev = Console.ForegroundColor;

        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  PostgreSQL:");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"    {config.PostgresConnectionString ?? "[не задана]"}");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine("  Redis:");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"    {config.RedisConnectionString ?? "[не задана]"}");

        Console.ForegroundColor = prev;
        ConsoleUI.PressAnyKey();
    }

    // ─── Вспомогательные ──────────────────────────────────────────────────

    private static string? MaskPassword(string? connStr)
    {
        if (string.IsNullOrWhiteSpace(connStr)) return null;
        return Regex.Replace(connStr, @"(?i)(password\s*=\s*)([^;,]+)", "$1***");
    }

    private static bool TryExtractEndpoint(string connString, out string host, out int port)
    {
        host = string.Empty;
        port = 6379;

        var firstToken = connString
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(t => !t.Contains('='));

        if (string.IsNullOrWhiteSpace(firstToken))
        {
            return false;
        }

        var lastColon = firstToken.LastIndexOf(':');
        if (lastColon <= 0 || lastColon == firstToken.Length - 1)
        {
            host = firstToken;
            return true;
        }

        host = firstToken[..lastColon];
        return int.TryParse(firstToken[(lastColon + 1)..], out port);
    }
}
