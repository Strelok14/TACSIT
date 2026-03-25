using TacidManager.Core;
using TacidManager.UI;

namespace TacidManager.Menus;

internal static class ConnectionMenu
{
    public static Task Run(EnvConfig config)
    {
        while (true)
        {
            ConsoleUI.Clear();
            ConsoleUI.PrintBanner();
            ConsoleUI.Header("Режим подключения");

            var env     = config.AspNetCoreEnvironment ?? "Production";
            var urls    = config.AspNetCoreUrls ?? "(не задан)";
            var httpOn  = config.AllowInsecureHttp;

            ConsoleUI.StatusRow("ASPNETCORE_ENVIRONMENT",   env);
            ConsoleUI.StatusRow("ASPNETCORE_URLS",          urls);
            ConsoleUI.StatusRow("TACID_ALLOW_INSECURE_HTTP", httpOn ? "true  ⚠ HTTP активен" : "false  ✓ HTTPS");
            Console.WriteLine();

            if (httpOn)
            {
                ConsoleUI.Warning("HTTP-режим ВКЛЮЧЁН — JWT-токены и телеметрия передаются открытым текстом.");
                ConsoleUI.Warning("Допустимо только в доверенной сети: LAN, WireGuard VPN.");
            }
            else
            {
                ConsoleUI.Info("HTTPS-режим. Клиенты обязаны подключаться по TLS.");
                ConsoleUI.Info("Настройте nginx reverse proxy + Let's Encrypt или самоподписанный сертификат.");
            }
            Console.WriteLine();

            ConsoleUI.Header("Пресеты сценариев");
            ConsoleUI.MenuItem(1, "Production + HTTPS  (nginx reverse proxy, TLS 1.3)");
            ConsoleUI.MenuItem(2, "Production + HTTP   (LAN / WireGuard VPN, без TLS)");
            ConsoleUI.MenuItem(3, "Production + HTTP   (публичный IP, проброс порта)");
            ConsoleUI.MenuItem(4, "Development         (SQLite, localhost, без TLS)");
            ConsoleUI.Header("Тонкая настройка");
            ConsoleUI.MenuItem(5, "Задать ASPNETCORE_URLS вручную");
            ConsoleUI.MenuItem(6, $"Переключить HTTP-режим          (сейчас: {(httpOn ? "ВКЛ ⚠" : "ВЫКЛ ✓")})");
            ConsoleUI.MenuItem(7, "Задать ASPNETCORE_ENVIRONMENT вручную");
            ConsoleUI.MenuItem(0, "Назад");

            var choice = ConsoleUI.SelectOption("Выберите", 7);
            switch (choice)
            {
                case 1: ApplyPreset_Https(config);     break;
                case 2: ApplyPreset_LanHttp(config);   break;
                case 3: ApplyPreset_PublicHttp(config); break;
                case 4: ApplyPreset_Dev(config);       break;
                case 5: SetUrlsManual(config);         break;
                case 6: ToggleHttp(config);            break;
                case 7: SetEnvironmentManual(config);  break;
                case 0: return Task.CompletedTask;
            }
        }
    }

    // ─── Пресеты ──────────────────────────────────────────────────────────

    private static void ApplyPreset_Https(EnvConfig config)
    {
        config.AspNetCoreEnvironment = "Production";
        config.AllowInsecureHttp     = false;
        // nginx проксирует 443→localhost:5001; сервер слушает только loopback
        config.AspNetCoreUrls        = "http://127.0.0.1:5001";
        ConsoleUI.Success("Применён пресет: Production + HTTPS (nginx front-end).");
        Console.WriteLine();
        ConsoleUI.Info("Сервер привязан к 127.0.0.1:5001 — снаружи недоступен напрямую.");
        ConsoleUI.Info("nginx слушает 0.0.0.0:443 и проксирует на 127.0.0.1:5001.");
        ConsoleUI.Info("Пример nginx location:");
        ConsoleUI.Info("  proxy_pass http://127.0.0.1:5001;");
        ConsoleUI.Info("  proxy_http_version 1.1;");
        ConsoleUI.Info("  proxy_set_header Upgrade $http_upgrade;");
        ConsoleUI.Info("  proxy_set_header Connection \"upgrade\";");
        ConsoleUI.PressAnyKey();
    }

    private static void ApplyPreset_LanHttp(EnvConfig config)
    {
        config.AspNetCoreEnvironment = "Production";
        config.AllowInsecureHttp     = true;
        config.AspNetCoreUrls        = "http://0.0.0.0:5001";
        ConsoleUI.Success("Применён пресет: Production + HTTP (LAN / WireGuard VPN).");
        Console.WriteLine();
        ConsoleUI.Warning("Трафик незашифрован — используйте только в той же сети или VPN-туннеле.");
        ConsoleUI.Info("WireGuard VPN рекомендуется для игры на полигоне без интернета:");
        ConsoleUI.Info("  Сервер: 10.77.0.1:5001  →  клиент вводит 10.77.0.1");
        ConsoleUI.Info("  Android авто-определит IP → http://");
        ConsoleUI.PressAnyKey();
    }

    private static void ApplyPreset_PublicHttp(EnvConfig config)
    {
        config.AspNetCoreEnvironment = "Production";
        config.AllowInsecureHttp     = true;
        config.AspNetCoreUrls        = "http://0.0.0.0:5001";
        ConsoleUI.Success("Применён пресет: Production + HTTP (публичный IP, без TLS).");
        Console.WriteLine();
        ConsoleUI.Warning("ВНИМАНИЕ: токены и данные передаются через интернет открытым текстом!");
        ConsoleUI.Warning("Этот режим подходит ТОЛЬКО для кратковременного тестирования.");
        ConsoleUI.Info("После теста переключитесь на HTTPS с Let's Encrypt или на VPN.");
        ConsoleUI.Info("Убедитесь что роутер пробрасывает TCP 5001 на этот сервер.");
        ConsoleUI.PressAnyKey();
    }

    private static void ApplyPreset_Dev(EnvConfig config)
    {
        config.AspNetCoreEnvironment = "Development";
        config.AllowInsecureHttp     = true;
        config.AspNetCoreUrls        = "http://localhost:5001";
        ConsoleUI.Success("Применён пресет: Development.");
        Console.WriteLine();
        ConsoleUI.Warning("Development = SQLite вместо PostgreSQL, Swagger UI включён.");
        ConsoleUI.Warning("Только для локальной разработки! Не использовать на рабочем сервере.");
        ConsoleUI.PressAnyKey();
    }

    // ─── Тонкая настройка ─────────────────────────────────────────────────

    private static void SetUrlsManual(EnvConfig config)
    {
        ConsoleUI.Info("Формат: http[s]://<хост>:<порт>");
        ConsoleUI.Info("  Все интерфейсы:  http://0.0.0.0:5001");
        ConsoleUI.Info("  Только loopback: http://127.0.0.1:5001");
        ConsoleUI.Info("  Несколько:       http://0.0.0.0:5001;https://0.0.0.0:5443");
        Console.WriteLine();

        var val = ConsoleUI.Prompt("ASPNETCORE_URLS (Enter = без изменений)", config.AspNetCoreUrls);
        if (string.IsNullOrWhiteSpace(val))
        {
            ConsoleUI.Info("Без изменений.");
            ConsoleUI.PressAnyKey();
            return;
        }

        // Базовая валидация
        var parts = val.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var invalid = parts.Where(p => !p.TrimStart().StartsWith("http://") && !p.TrimStart().StartsWith("https://")).ToList();
        if (invalid.Count > 0)
        {
            ConsoleUI.Error($"Некорректный URL: '{invalid[0]}'. Должен начинаться с http:// или https://");
            ConsoleUI.PressAnyKey();
            return;
        }

        config.AspNetCoreUrls = val.Trim();
        ConsoleUI.Success($"ASPNETCORE_URLS = {val.Trim()}");
        ConsoleUI.PressAnyKey();
    }

    private static void ToggleHttp(EnvConfig config)
    {
        if (!config.AllowInsecureHttp)
        {
            ConsoleUI.Warning("Включение HTTP-режима: токены будут передаваться открытым текстом!");
            if (!ConsoleUI.Confirm("Включить HTTP-режим (TACID_ALLOW_INSECURE_HTTP=true)?", defaultYes: false))
            {
                ConsoleUI.Info("Отменено.");
                ConsoleUI.PressAnyKey();
                return;
            }
        }

        config.AllowInsecureHttp = !config.AllowInsecureHttp;
        var state = config.AllowInsecureHttp ? "ВКЛЮЧЁН ⚠ (HTTP)" : "ВЫКЛЮЧЕН ✓ (HTTPS required)";
        ConsoleUI.Success($"HTTP-режим: {state}");
        ConsoleUI.PressAnyKey();
    }

    private static void SetEnvironmentManual(EnvConfig config)
    {
        ConsoleUI.Info("Доступные значения: Production, Development, Staging");
        ConsoleUI.Warning("Production = PostgreSQL;  Development = SQLite + Swagger.");
        var val = ConsoleUI.Prompt("ASPNETCORE_ENVIRONMENT", config.AspNetCoreEnvironment);
        if (!string.IsNullOrWhiteSpace(val))
        {
            config.AspNetCoreEnvironment = val.Trim();
            ConsoleUI.Success($"ASPNETCORE_ENVIRONMENT = {val.Trim()}");
        }
        else ConsoleUI.Info("Без изменений.");
        ConsoleUI.PressAnyKey();
    }
}
