using TacidManager.Core;
using TacidManager.UI;

namespace TacidManager.Menus;

internal static class UsersMenu
{
    public static Task Run(EnvConfig config)
    {
        while (true)
        {
            ConsoleUI.Clear();
            ConsoleUI.PrintBanner();
            ConsoleUI.Header("Учётные данные пользователей");

            ConsoleUI.StatusRow("admin  логин",     config.AdminLogin);
            ConsoleUI.StatusRow("admin  пароль",    config.AdminPassword,    isPassword: true);
            ConsoleUI.StatusRow("observer  логин",  config.ObserverLogin);
            ConsoleUI.StatusRow("observer  пароль", config.ObserverPassword, isPassword: true);
            ConsoleUI.StatusRow("player  логин",    config.PlayerLogin);
            ConsoleUI.StatusRow("player  пароль",   config.PlayerPassword,   isPassword: true);
            ConsoleUI.StatusRow("player  BeaconID", config.PlayerBeaconId);
            Console.WriteLine();

            ConsoleUI.Info("admin    — полный доступ: пользователи, GPS, detections, offline deployment.");
            ConsoleUI.Info("observer — просмотр карты, истории, SignalR/Web UI.");
            ConsoleUI.Info("player   — Android клиент: GPS + detections + JWT/HMAC.");
            ConsoleUI.Warning("Меню задаёт bootstrap-учётки для первого запуска; после старта сервер хранит пользователей в БД.");
            ConsoleUI.Warning("Права файла должны быть 640 (только root + группа strikeball).");
            Console.WriteLine();

            ConsoleUI.Header("Изменить");
            ConsoleUI.MenuItem(1, "Администратор (admin)");
            ConsoleUI.MenuItem(2, "Наблюдатель   (observer)");
            ConsoleUI.MenuItem(3, "Игрок         (player)");
            ConsoleUI.MenuItem(0, "Назад");

            var choice = ConsoleUI.SelectOption("Выберите", 3);
            switch (choice)
            {
                case 1: EditAdmin(config); break;
                case 2: EditObserver(config); break;
                case 3: EditPlayer(config); break;
                case 0: return Task.CompletedTask;
            }
        }
    }

    // ─── Admin ────────────────────────────────────────────────────────────

    private static void EditAdmin(EnvConfig config)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("Администратор (admin)");
            ConsoleUI.Info("Имеет доступ к пользователям, offline deployment и админским API GPS-демо.");
        Console.WriteLine();

        var login = ConsoleUI.Prompt("Логин (Enter = без изменений)", config.AdminLogin);
        if (!string.IsNullOrWhiteSpace(login))
            config.AdminLogin = login.Trim();

        var pass = ConsoleUI.PromptPassword("Пароль (Enter = без изменений)");
        if (!string.IsNullOrWhiteSpace(pass))
        {
            if (pass.Length < 12)
            {
                ConsoleUI.Warning($"Пароль короткий ({pass.Length} символов). Рекомендуется ≥ 12.");
                if (!ConsoleUI.Confirm("Использовать всё равно?", defaultYes: false))
                {
                    ConsoleUI.Info("Пароль не изменён.");
                    ConsoleUI.PressAnyKey();
                    return;
                }
            }
            config.AdminPassword = pass;
        }

        ConsoleUI.Success("Данные администратора обновлены.");
        ConsoleUI.PressAnyKey();
    }

    // ─── Observer ─────────────────────────────────────────────────────────

    private static void EditObserver(EnvConfig config)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("Наблюдатель (observer)");
            ConsoleUI.Info("Права: подключение к SignalR Hub, веб-карте и истории перемещений.");
            ConsoleUI.Info("Используется на ноутбуке наблюдателя или КПК без отправки GPS.");
        Console.WriteLine();

        var login = ConsoleUI.Prompt("Логин (Enter = без изменений)", config.ObserverLogin);
        if (!string.IsNullOrWhiteSpace(login))
            config.ObserverLogin = login.Trim();

        var pass = ConsoleUI.PromptPassword("Пароль (Enter = без изменений)");
        if (!string.IsNullOrWhiteSpace(pass))
        {
            if (pass.Length < 8)
            {
                ConsoleUI.Warning($"Пароль короткий ({pass.Length} символов).");
                if (!ConsoleUI.Confirm("Использовать?", defaultYes: false))
                {
                    ConsoleUI.Info("Пароль не изменён.");
                    ConsoleUI.PressAnyKey();
                    return;
                }
            }
            config.ObserverPassword = pass;
        }

        ConsoleUI.Success("Данные observer обновлены.");
        ConsoleUI.PressAnyKey();
    }

    // ─── Player ───────────────────────────────────────────────────────────

    private static void EditPlayer(EnvConfig config)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("Игрок (player)");
            ConsoleUI.Info("Права: отправка GPS POST /api/gps и detections POST /api/detections.");
            ConsoleUI.Info("Legacy BeaconID сохранён только для совместимости со старым UWB API.");
            ConsoleUI.Info("Для GPS ветки JWT связывается с user_id, а HMAC ключ выдаётся при логине.");
        Console.WriteLine();

        var login = ConsoleUI.Prompt("Логин (Enter = без изменений)", config.PlayerLogin);
        if (!string.IsNullOrWhiteSpace(login))
            config.PlayerLogin = login.Trim();

        var pass = ConsoleUI.PromptPassword("Пароль (Enter = без изменений)");
        if (!string.IsNullOrWhiteSpace(pass))
        {
            if (pass.Length < 8)
            {
                ConsoleUI.Warning($"Пароль короткий ({pass.Length} символов).");
                if (!ConsoleUI.Confirm("Использовать?", defaultYes: false))
                {
                    ConsoleUI.Info("Пароль не изменён.");
                    ConsoleUI.PressAnyKey();
                    return;
                }
            }
            config.PlayerPassword = pass;
        }

        var beaconStr = ConsoleUI.Prompt("BeaconID (целое число, Enter = без изменений)", config.PlayerBeaconId);
        if (!string.IsNullOrWhiteSpace(beaconStr))
        {
            if (int.TryParse(beaconStr.Trim(), out var bid) && bid > 0)
                config.PlayerBeaconId = bid.ToString();
            else
                ConsoleUI.Error("BeaconID должен быть положительным целым числом. Значение не изменено.");
        }

        ConsoleUI.Success("Данные player обновлены.");
        ConsoleUI.PressAnyKey();
    }
}
