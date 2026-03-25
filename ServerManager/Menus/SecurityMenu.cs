using System.Globalization;
using TacidManager.Core;
using TacidManager.UI;

namespace TacidManager.Menus;

/// <summary>
/// Параметры безопасности, которые можно переопределить ENV-переменными
/// без изменения appsettings.json.
///
/// Соответствие ENV-переменных → разделам appsettings.json (ASP.NET Core иерархия):
///   TelemetrySecurity__MaxTimestampDriftMs  → TelemetrySecurity:MaxTimestampDriftMs
///   TelemetrySecurity__MaxBacklogAgeMs      → TelemetrySecurity:MaxBacklogAgeMs
///   TelemetrySecurity__MaxSpeedMetersPerSec → TelemetrySecurity:MaxSpeedMetersPerSec
///   Security__KeyRotationDays               → Security:KeyRotationDays
///   Security__PreviousKeyGraceDays          → Security:PreviousKeyGraceDays
/// </summary>
internal static class SecurityMenu
{
    // Пары: (ENV-ключ, описание, единица, мин, макс, default)
    private static readonly (string Key, string Label, string Unit, double Min, double Max, double Default)[] Params =
    [
        ("TelemetrySecurity__MaxTimestampDriftMs",
         "Максимальный сдвиг timestamp пакета",
         "мс", 100, 30_000, 5_000),

        ("TelemetrySecurity__MaxBacklogAgeMs",
         "Максимальный возраст пакета из буфера маяка",
         "мс", 5_000, 600_000, 120_000),

        ("TelemetrySecurity__MaxSpeedMetersPerSec",
         "Максимальная физическая скорость игрока",
         "м/с", 1.0, 50.0, 10.0),

        ("Security__KeyRotationDays",
         "Период ротации HMAC-ключей маяков",
         "дней", 1, 365, 30),

        ("Security__PreviousKeyGraceDays",
         "Грейс-период старого ключа при ротации",
         "дней", 1, 60, 7),
    ];

    public static Task Run(EnvConfig config)
    {
        while (true)
        {
            ConsoleUI.Clear();
            ConsoleUI.PrintBanner();
            ConsoleUI.Header("Параметры безопасности");
            ConsoleUI.Info("ENV-переменные переопределяют значения appsettings.json.");
            ConsoleUI.Info("Пустое значение = использовать умолчание из appsettings.");
            Console.WriteLine();

            // Показываем текущие значения с нумерацией
            for (var i = 0; i < Params.Length; i++)
            {
                var (key, label, unit, _, _, def) = Params[i];
                var raw = config.Get(key);
                var display = string.IsNullOrWhiteSpace(raw)
                    ? $"{def} {unit}  (умолчание)"
                    : $"{raw} {unit}  (переопределено)";
                ConsoleUI.StatusRow($"[{i + 1}] {label}", display);
            }
            Console.WriteLine();

            ConsoleUI.Info("Защита от replay (Доклад, §3.1):");
            ConsoleUI.Info("  MaxTimestampDriftMs — отбраковывает пакеты с неактуальным временем.");
            ConsoleUI.Info("  MaxBacklogAgeMs     — принимает буферизованные пакеты маяка при LTE-пропадании.");
            ConsoleUI.Info("  Формула: |packet.ts - now| ≤ drift  ИЛИ  age ≤ backlog (пакет из буфера).");
            Console.WriteLine();

            ConsoleUI.Header("Действия");
            for (var i = 0; i < Params.Length; i++)
                ConsoleUI.MenuItem(i + 1, Params[i].Label);
            ConsoleUI.MenuItem(Params.Length + 1, "Сбросить все к значениям appsettings (удалить переопределения)");
            ConsoleUI.MenuItem(0, "Назад");

            var max = Params.Length + 1;
            var choice = ConsoleUI.SelectOption("Выберите", max);

            if (choice == 0) return Task.CompletedTask;

            if (choice == max)
            {
                ResetAll(config);
                continue;
            }

            EditParam(config, Params[choice - 1]);
        }
    }

    // ─── Редактирование параметра ─────────────────────────────────────────

    private static void EditParam(
        EnvConfig config,
        (string Key, string Label, string Unit, double Min, double Max, double Default) p)
    {
        ConsoleUI.Clear();
        ConsoleUI.PrintBanner();
        ConsoleUI.Header(p.Label);

        var current = config.Get(p.Key);
        ConsoleUI.Info($"Текущее:   {(string.IsNullOrWhiteSpace(current) ? $"{p.Default} {p.Unit} (умолчание)" : $"{current} {p.Unit} (переопределено)")}");
        ConsoleUI.Info($"Диапазон:  {p.Min}–{p.Max} {p.Unit}");
        ConsoleUI.Info($"Умолчание: {p.Default} {p.Unit} (из appsettings.json)");
        Console.WriteLine();

        var input = ConsoleUI.Prompt($"Новое значение (Enter = без изменений, '-' = удалить переопределение)");
        if (string.IsNullOrWhiteSpace(input))
        {
            ConsoleUI.Info("Без изменений.");
            ConsoleUI.PressAnyKey();
            return;
        }

        if (input.Trim() == "-")
        {
            config.Remove(p.Key);
            ConsoleUI.Success($"Переопределение удалено. Будет использоваться {p.Default} {p.Unit} из appsettings.");
            ConsoleUI.PressAnyKey();
            return;
        }

        if (!double.TryParse(input.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val)
            || val < p.Min || val > p.Max)
        {
            ConsoleUI.Error($"Значение вне диапазона {p.Min}–{p.Max} {p.Unit}.");
            ConsoleUI.PressAnyKey();
            return;
        }

        // Целочисленные параметры хранить без дробной части
        var stored = (val == Math.Floor(val))
            ? ((long)val).ToString()
            : val.ToString(CultureInfo.InvariantCulture);

        config.Set(p.Key, stored);
        ConsoleUI.Success($"{p.Key} = {stored} {p.Unit}");
        ConsoleUI.PressAnyKey();
    }

    // ─── Сброс ────────────────────────────────────────────────────────────

    private static void ResetAll(EnvConfig config)
    {
        if (!ConsoleUI.Confirm(
            "Удалить все ENV-переопределения параметров безопасности?", defaultYes: false))
        {
            ConsoleUI.PressAnyKey();
            return;
        }

        foreach (var (key, _, _, _, _, _) in Params)
            config.Remove(key);

        ConsoleUI.Success("Все переопределения удалены. Используются значения из appsettings.json.");
        ConsoleUI.PressAnyKey();
    }
}
