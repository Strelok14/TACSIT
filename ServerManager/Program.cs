using TacidManager.Core;
using TacidManager.Menus;
using TacidManager.UI;
using System.Runtime.InteropServices;

// ─── Точка входа ──────────────────────────────────────────────────────────────

var envFilePath = ResolveEnvFilePath(args);

ConsoleUI.Clear();
ConsoleUI.PrintBanner();

// Защита: не запускаем без явного подтверждения если нет прав на файл
if (OperatingSystem.IsLinux())
{
    var defaultLinux = "/etc/strikeball/environment";
    if (envFilePath == defaultLinux && !IsRunningAsRoot())
    {
        ConsoleUI.Warning("Файл по умолчанию /etc/strikeball/environment требует root.");
        ConsoleUI.Info("Запустите: sudo tacid-manager");
        ConsoleUI.Info("Или укажите другой файл: tacid-manager --env-file ./server.env");
        Console.WriteLine();
    }
}

EnvConfig config;

if (File.Exists(envFilePath))
{
    try
    {
        config = EnvConfig.Load(envFilePath);
        ConsoleUI.Success($"Загружен: {envFilePath}");
    }
    catch (Exception ex)
    {
        ConsoleUI.Error($"Ошибка чтения {envFilePath}: {ex.Message}");
        ConsoleUI.Info("Будет создана новая конфигурация на основе шаблона.");
        config = EnvConfig.CreateTemplate();
    }
}
else
{
    ConsoleUI.Warning($"Файл не найден: {envFilePath}");
    ConsoleUI.Info("Будет создана новая конфигурация на основе шаблона.");
    ConsoleUI.Info($"После настройки сохраните её через меню [7].");
    config = EnvConfig.CreateTemplate();
}

Console.WriteLine();
ConsoleUI.Info("Нажмите любую клавишу для входа в меню...");
Console.ReadKey(intercept: true);

await MainMenu.Run(config, envFilePath);

// ─── Определение пути к файлу конфигурации ────────────────────────────────

static string ResolveEnvFilePath(string[] args)
{
    // 1) CLI: --env-file=path  или  --env-file path
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--env-file=", StringComparison.OrdinalIgnoreCase))
            return args[i]["--env-file=".Length..].Trim();
        if (string.Equals(args[i], "--env-file", StringComparison.OrdinalIgnoreCase)
            && i + 1 < args.Length)
            return args[i + 1].Trim();
    }

    // 2) ENV-переменная
    var envVar = Environment.GetEnvironmentVariable("TACID_ENV_FILE");
    if (!string.IsNullOrWhiteSpace(envVar))
        return envVar.Trim();

    // 3) Linux production default
    if (OperatingSystem.IsLinux())
    {
        const string linuxDefault = "/etc/strikeball/environment";
        if (File.Exists(linuxDefault))
            return linuxDefault;
    }

    // 4) Рядом с бинарником
    return Path.Combine(AppContext.BaseDirectory, "server.env");
}

static bool IsRunningAsRoot()
{
    if (!OperatingSystem.IsLinux()) return false;

    try
    {
        return geteuid() == 0;
    }
    catch
    {
        // Fallback для сред, где P/Invoke недоступен.
        return string.Equals(Environment.UserName, "root", StringComparison.Ordinal);
    }
}

[DllImport("libc")]
static extern uint geteuid();
