using TacidManager.Core;
using TacidManager.UI;

namespace TacidManager.Menus;

internal static class KeysMenu
{
    public static Task Run(EnvConfig config)
    {
        while (true)
        {
            ConsoleUI.Clear();
            ConsoleUI.PrintBanner();
            ConsoleUI.Header("Управление ключами");

            ConsoleUI.StatusRow("JWT Signing Key",     config.JwtSigningKey,  isPassword: true);
            ConsoleUI.StatusRow("Master Key (AES-256)", config.MasterKeyB64,  isPassword: true);
            Console.WriteLine();

            ConsoleUI.Info("JWT Key    — подписывает access-токены (HMAC-SHA256). Минимум 32 байта UTF-8.");
            ConsoleUI.Info("Master Key — шифрует пользовательские HMAC-ключи (AES-256-GCM). Ровно 32 байта.");
            ConsoleUI.Warning("Замена активного ключа делает все текущие JWT и HMAC-ключи пользователей недействительными!");
            Console.WriteLine();

            ConsoleUI.Header("Действия");
            ConsoleUI.MenuItem(1, "Сгенерировать новый JWT Signing Key  (авто, 48 байт CSPRNG)");
            ConsoleUI.MenuItem(2, "Задать JWT Signing Key вручную");
            ConsoleUI.MenuItem(3, "Сгенерировать новый Master Key        (авто, AES-256)");
            ConsoleUI.MenuItem(4, "Задать Master Key вручную             (Base64 32 байта)");
            ConsoleUI.MenuItem(5, "Сгенерировать HMAC ключ клиента       (для Android / API)");
            ConsoleUI.MenuItem(0, "Назад");

            var choice = ConsoleUI.SelectOption("Выберите", 5);
            switch (choice)
            {
                case 1: GenerateJwtKey(config); break;
                case 2: SetJwtKeyManual(config); break;
                case 3: GenerateMasterKey(config); break;
                case 4: SetMasterKeyManual(config); break;
                case 5: GenerateBeaconKey(); break;
                case 0: return Task.CompletedTask;
            }
        }
    }

    // ─── JWT ──────────────────────────────────────────────────────────────

    private static void GenerateJwtKey(EnvConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.JwtSigningKey))
        {
            ConsoleUI.Warning("JWT Signing Key уже задан.");
            ConsoleUI.Warning("После замены все выданные токены (access + refresh) станут недействительными.");
            if (!ConsoleUI.Confirm("Перегенерировать?", defaultYes: false))
            {
                ConsoleUI.Info("Отменено.");
                ConsoleUI.PressAnyKey();
                return;
            }
        }

        var key = CryptoUtils.GenerateJwtSigningKey();
        config.JwtSigningKey = key;
        ConsoleUI.Success($"JWT Key сгенерирован ({key.Length} символов URL-safe Base64).");
        ConsoleUI.Info("Сохраните конфигурацию (п.7 главного меню) и перезапустите сервер.");
        ConsoleUI.PressAnyKey();
    }

    private static void SetJwtKeyManual(EnvConfig config)
    {
        ConsoleUI.Info("Введите JWT Signing Key (минимум 32 символа). Ввод скрыт.");
        var key = ConsoleUI.PromptPassword("JWT Key");

        if (string.IsNullOrWhiteSpace(key))
        {
            ConsoleUI.Info("Отменено.");
            ConsoleUI.PressAnyKey();
            return;
        }

        if (!CryptoUtils.IsValidJwtKey(key))
        {
            ConsoleUI.Error("Ключ слишком короткий — требуется минимум 32 байта в UTF-8.");
            ConsoleUI.PressAnyKey();
            return;
        }

        config.JwtSigningKey = key;
        ConsoleUI.Success("JWT Key задан.");
        ConsoleUI.PressAnyKey();
    }

    // ─── Master Key ────────────────────────────────────────────────────────

    private static void GenerateMasterKey(EnvConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.MasterKeyB64))
        {
            ConsoleUI.Warning("Master Key уже задан.");
            ConsoleUI.Warning("После замены ВСЕ зашифрованные ключи маяков в БД станут нечитаемыми.");
            ConsoleUI.Warning("Потребуется повторная прошивка маяков и загрузка новых ключей через API.");
            if (!ConsoleUI.Confirm("Всё равно перегенерировать?", defaultYes: false))
            {
                ConsoleUI.Info("Отменено.");
                ConsoleUI.PressAnyKey();
                return;
            }
        }

        var key = CryptoUtils.GenerateMasterKey();
        config.MasterKeyB64 = key;

        ConsoleUI.Success("Master Key сгенерирован (32 байта, AES-256-GCM).");
        Console.WriteLine();
        ConsoleUI.Warning("╔═══════════════════════════════════════════════════════════╗");
        ConsoleUI.Warning("║  СОЗДАЙТЕ РЕЗЕРВНУЮ КОПИЮ ЭТОГО КЛЮЧА ПРЯМО СЕЙЧАС!      ║");
        ConsoleUI.Warning("║  Без него расшифровка ключей маяков в БД НЕВОЗМОЖНА.     ║");
        ConsoleUI.Warning("╚═══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  {key}");
        Console.ForegroundColor = prev;
        Console.WriteLine();

        ConsoleUI.PressAnyKey();
    }

    private static void SetMasterKeyManual(EnvConfig config)
    {
        ConsoleUI.Info("Введите Master Key в формате Base64 (32 байта → 44 символа стандартного Base64).");
        var key = ConsoleUI.Prompt("Master Key (Base64)");

        if (string.IsNullOrWhiteSpace(key))
        {
            ConsoleUI.Info("Отменено.");
            ConsoleUI.PressAnyKey();
            return;
        }

        if (!CryptoUtils.IsValidMasterKey(key))
        {
            ConsoleUI.Error("Некорректный Base64 или неверная длина (требуется ровно 32 байта).");
            ConsoleUI.Info("Пример: openssl rand -base64 32");
            ConsoleUI.PressAnyKey();
            return;
        }

        config.MasterKeyB64 = key;
        ConsoleUI.Success("Master Key задан.");
        ConsoleUI.PressAnyKey();
    }

    // ─── Beacon Key ────────────────────────────────────────────────────────

    private static void GenerateBeaconKey()
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header("Генерация HMAC ключа клиента (HMAC-SHA256)");

        var key = CryptoUtils.GenerateBeaconKey();

        ConsoleUI.Success("Ключ клиента сгенерирован (32 байта, HMAC-SHA256 совместимый):");
        Console.WriteLine();

        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  {key}");
        Console.ForegroundColor = prev;
        Console.WriteLine();

        ConsoleUI.Info("Используйте этот ключ двумя способами:");
        Console.WriteLine();
        ConsoleUI.Info("  1) Загрузить в БД для пользователя (рекомендуется):");
        ConsoleUI.Info("     Через IUserHmacKeyStore / админский API локального демо");
        ConsoleUI.Info("     Заголовок: Authorization: Bearer <admin-token>");
        Console.WriteLine();
        ConsoleUI.Info("  2) Сохранить как аварийную копию в офлайн-сейфе ключей.");
        ConsoleUI.Warning("Для GPS ветки сервер сам генерирует и шифрует HMAC ключ при первом логине пользователя.");
        ConsoleUI.PressAnyKey();
    }
}
