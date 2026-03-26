using TacidManager.Core;
using TacidManager.UI;

namespace TacidManager.Menus;

internal static class MainMenu
{
    public static async Task Run(EnvConfig config, string envFilePath)
    {
        while (true)
        {
            ConsoleUI.Clear();
            ConsoleUI.PrintBanner();

            // ─── Сводка состояния ─────────────────────────────────────────
            ConsoleUI.Header("Текущее состояние");
            ConsoleUI.StatusRow("JWT Signing Key",    config.JwtSigningKey,   isPassword: true);
            ConsoleUI.StatusRow("Master Key (AES-256)", config.MasterKeyB64,  isPassword: true);
            ConsoleUI.StatusRow("Admin",              FormatUser(config.AdminLogin, config.AdminPassword));
            ConsoleUI.StatusRow("Режим",              BuildModeString(config));
            ConsoleUI.StatusRow("Файл конфигурации",  envFilePath);
            ConsoleUI.SectionEnd();

            // ─── Предупреждения (только критические) ─────────────────────
            var issues = config.Validate();
            var errors = issues.Where(i => i.Level == Severity.Error).ToList();
            if (errors.Count > 0)
            {
                foreach (var e in errors.Take(3))
                    ConsoleUI.Issue(e);
                Console.WriteLine();
            }

            // ─── Главное меню ─────────────────────────────────────────────
            ConsoleUI.Header("Меню");
            ConsoleUI.MenuItem(1, "Управление ключами              (JWT, Master Key, маяки)");
            ConsoleUI.MenuItem(2, "Учётные данные                  (admin / observer / player)");
            ConsoleUI.MenuItem(3, "Режим подключения               (HTTP / HTTPS, порт, URL)");
            ConsoleUI.MenuItem(4, "База данных и Redis             (PostgreSQL, Redis)");
            ConsoleUI.MenuItem(5, "Параметры безопасности          (timestamp, rate limit, ротация)");
            ConsoleUI.MenuItem(6, "Проверить конфигурацию          (полная диагностика)");
            ConsoleUI.MenuItem(7, $"Сохранить файл                  [{envFilePath}]");
            ConsoleUI.MenuItem(8, "Показать env-файл               (вывести содержимое)");
            ConsoleUI.MenuItem(0, "Выход");

            var choice = ConsoleUI.SelectOption("Выберите раздел", 8);
            switch (choice)
            {
                case 1: await KeysMenu.Run(config); break;
                case 2: await UsersMenu.Run(config); break;
                case 3: await ConnectionMenu.Run(config); break;
                case 4: await DatabaseMenu.Run(config); break;
                case 5: await SecurityMenu.Run(config); break;
                case 6: ShowValidation(config); ConsoleUI.PressAnyKey(); break;
                case 7: SaveConfig(config, envFilePath); ConsoleUI.PressAnyKey(); break;
                case 8: ShowExport(config); ConsoleUI.PressAnyKey(); break;
                case 0:
                    if (HasUnsavedWarnings(config, envFilePath))
                        continue;
                    return;
            }
        }
    }

    // ─── Хелперы ──────────────────────────────────────────────────────────

    private static string BuildModeString(EnvConfig config)
    {
        var env  = config.AspNetCoreEnvironment ?? "Production";
        var http = config.AllowInsecureHttp ? "⚠ HTTP (LAN)" : "HTTPS";
        var url  = config.AspNetCoreUrls ?? "not set";
        return $"{env} | {http} | {url}";
    }

    private static string? FormatUser(string? login, string? password)
    {
        if (string.IsNullOrWhiteSpace(login)) return null;
        return password is { Length: > 0 } ? login : $"{login} (пароль не задан!)";
    }

    private static void ShowValidation(EnvConfig config)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("Полная диагностика конфигурации");

        var issues = config.Validate();
        if (issues.Count == 0)
        {
            ConsoleUI.Success("Конфигурация корректна. Все обязательные параметры заданы.");
            Console.WriteLine();
            ConsoleUI.Info("Сервер готов к запуску в выбранном режиме.");
        }
        else
        {
            foreach (var issue in issues.OrderByDescending(i => i.Level))
                ConsoleUI.Issue(issue);

            Console.WriteLine();
            var e = issues.Count(i => i.Level == Severity.Error);
            var w = issues.Count(i => i.Level == Severity.Warning);
            var info = issues.Count(i => i.Level == Severity.Info);
            if (e > 0)    ConsoleUI.Error($"Критических ошибок: {e}  — сервер не запустится");
            if (w > 0)    ConsoleUI.Warning($"Предупреждений: {w}");
            if (info > 0) ConsoleUI.Info($"Информационных: {info}");
        }
    }

    private static void SaveConfig(EnvConfig config, string path)
    {
        try
        {
            var backupPath = config.Save(path, createBackup: true);
            ConsoleUI.Success($"Файл сохранён: {path}");

            if (!string.IsNullOrWhiteSpace(backupPath))
                ConsoleUI.Info($"Backup создан: {backupPath}");

            if (OperatingSystem.IsLinux())
            {
                ConsoleUI.Info("Рекомендуется: chmod 640 && chown root:strikeball");
                ConsoleUI.Info($"  sudo chmod 640 {path}");
                ConsoleUI.Info($"  sudo chown root:strikeball {path}");
            }
        }
        catch (UnauthorizedAccessException)
        {
            ConsoleUI.Error($"Нет прав на запись: {path}");
            ConsoleUI.Info("Запустите tacid-manager от имени root или с sudo.");
        }
        catch (Exception ex)
        {
            ConsoleUI.Error($"Ошибка: {ex.Message}");
        }
    }

    private static void ShowExport(EnvConfig config)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("Содержимое env-файла");
        ConsoleUI.Warning("Файл содержит секреты — не оставляйте его на экране без присмотра!");
        Console.WriteLine();

        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(config.ExportText());
        Console.ForegroundColor = prev;
    }

    private static bool HasUnsavedWarnings(EnvConfig config, string path)
    {
        if (!config.IsDirty)
            return false;

        ConsoleUI.Warning("Есть несохранённые изменения конфигурации.");
        ConsoleUI.Info($"Файл: {path}");

        var exitWithoutSave = ConsoleUI.Confirm("Выйти без сохранения?", defaultYes: false);
        return !exitWithoutSave;
    }
}
