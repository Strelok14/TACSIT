using System.Text.RegularExpressions;
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
            ConsoleUI.MenuItem(0, "Назад");

            var choice = ConsoleUI.SelectOption("Выберите", 3);
            switch (choice)
            {
                case 1: EditPostgres(config); break;
                case 2: EditRedis(config);    break;
                case 3: ShowRaw(config);      break;
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
        return Regex.Replace(connStr, @"(?i)(password\s*=\s*)([^;]+)", "$1***");
    }
}
