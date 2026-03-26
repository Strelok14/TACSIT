using System.Text;
using TacidManager.Core;

namespace TacidManager.UI;

/// <summary>
/// Консольный UI — цвета, компоновка строк, ввод с маскировкой, выбор пункта меню.
/// Не зависит от внешних библиотек: только стандартный Console API.
/// </summary>
internal static class ConsoleUI
{
    // ─── Цветовая схема ────────────────────────────────────────────────────

    private static readonly ConsoleColor ColTitle   = ConsoleColor.Cyan;
    private static readonly ConsoleColor ColLabel   = ConsoleColor.White;
    private static readonly ConsoleColor ColOk      = ConsoleColor.Green;
    private static readonly ConsoleColor ColMissing = ConsoleColor.Red;
    private static readonly ConsoleColor ColWarn    = ConsoleColor.Yellow;
    private static readonly ConsoleColor ColInfo    = ConsoleColor.DarkCyan;
    private static readonly ConsoleColor ColDim     = ConsoleColor.DarkGray;
    private static readonly ConsoleColor ColMenu    = ConsoleColor.Cyan;
    private static readonly ConsoleColor ColSecret  = ConsoleColor.DarkGreen;

    private static int Width => Math.Min(Math.Max(Console.WindowWidth, 50), 90);

    // ─── Структурные элементы ─────────────────────────────────────────────

    public static void Clear() => Console.Clear();

    public static void PrintBanner()
    {
        var w = Width;
        Ln(ColTitle, new string('═', w));
        Ln(ColTitle,  "  ████████╗ █████╗  ██████╗██╗██████╗ ");
        Ln(ColTitle,  "     ██╔══╝██╔══██╗██╔════╝██║██╔══██╗");
        Ln(ColTitle,  "     ██║   ███████║██║     ██║██║  ██║");
        Ln(ColTitle,  "     ██║   ██╔══██║██║     ██║██║  ██║");
        Ln(ColTitle,  "     ██║   ██║  ██║╚██████╗██║██████╔╝");
        Ln(ColTitle,  "     ╚═╝   ╚═╝  ╚═╝ ╚═════╝╚═╝╚═════╝   Server Manager v1.0");
        Ln(ColDim,    "  Tactical Combat Identification Device — конфигурация сервера");
        Ln(ColTitle, new string('═', w));
        Console.WriteLine();
    }

    public static void Header(string title)
    {
        Console.WriteLine();
        W(ColTitle, $"  ┌─ {title} ");
        Ln(ColDim, new string('─', Math.Max(0, Width - title.Length - 7)));
        Console.WriteLine();
    }

    public static void SectionEnd()
    {
        Ln(ColDim, "  " + new string('─', Width - 2));
        Console.WriteLine();
    }

    // ─── Строки статуса ──────────────────────────────────────────────────

    /// <summary>Одна строка KEY / VALUE с индикатором ✓ / ✗</summary>
    public static void StatusRow(string label, string? value, bool isPassword = false)
    {
        const int lw = 36;
        W(ColDim, "  ");
        W(ColLabel, label.PadRight(lw));

        if (string.IsNullOrWhiteSpace(value))
        {
            Ln(ColMissing, "  ✗  НЕ ЗАДАН");
        }
        else if (isPassword)
        {
            var dots = new string('●', Math.Min(value.Length, 14));
            Ln(ColSecret, $"  ✓  {dots}");
        }
        else
        {
            var display = value.Length > 38 ? value[..14] + "…" + value[^10..] : value;
            Ln(ColOk, $"  ✓  {display}");
        }
    }

    // ─── Сообщения ────────────────────────────────────────────────────────

    public static void Error(string msg)   => Ln(ColMissing, $"  ✗  {msg}");
    public static void Success(string msg) => Ln(ColOk,      $"  ✓  {msg}");
    public static void Warning(string msg) => Ln(ColWarn,    $"  ⚠  {msg}");
    public static void Info(string msg)    => Ln(ColInfo,    $"  ●  {msg}");

    public static void Issue(ConfigIssue issue)
    {
        var (icon, col) = issue.Level switch
        {
            Severity.Error   => ("✗", ColMissing),
            Severity.Warning => ("⚠", ColWarn),
            _                => ("●", ColInfo)
        };
        W(col, $"  {icon}  ");
        W(ColDim, $"[{issue.Key}]  ");
        Ln(ConsoleColor.Gray, issue.Message);
    }

    // ─── Элементы меню ────────────────────────────────────────────────────

    public static void MenuItem(int num, string text)
    {
        W(ColMenu, $"  [{num}] ");
        Ln(ColLabel, text);
    }

    public static void MenuItemInfo(string text)
        => Ln(ColDim, $"  [─] {text}");

    // ─── Ввод ─────────────────────────────────────────────────────────────

    /// <summary>Строка ввода с опциональным показом текущего значения.</summary>
    public static string? Prompt(string label, string? current = null)
    {
        if (current != null)
            Ln(ColDim, $"  Текущее: {(current.Length > 0 ? $"\"{TruncateForDisplay(current)}\"" : "[не задано]")}");
        W(ConsoleColor.White, $"  {label}: ");
        return Console.ReadLine();
    }

    /// <summary>Ввод пароля с маскировкой символов (●).</summary>
    public static string? PromptPassword(string label)
    {
        W(ConsoleColor.White, $"  {label}: ");
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0) { sb.Remove(sb.Length - 1, 1); Console.Write("\b \b"); }
            }
            else if (key.KeyChar >= 0x20)   // printable char
            {
                sb.Append(key.KeyChar);
                Console.Write('●');
            }
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>Запрос подтверждения [Y/n] или [y/N].</summary>
    public static bool Confirm(string prompt, bool defaultYes = true)
    {
        var hint = defaultYes ? "[Y/n]" : "[y/N]";
        W(ColWarn, $"  {prompt} {hint}: ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(input)) return defaultYes;
        return input is "y" or "yes" or "д" or "да";
    }

    /// <summary>Числовой выбор пункта меню.</summary>
    public static int SelectOption(string prompt, int max, int min = 0)
    {
        while (true)
        {
            W(ConsoleColor.White, $"\n  {prompt} [{min}–{max}]: ");
            var input = Console.ReadLine()?.Trim();
            if (int.TryParse(input, out var choice) && choice >= min && choice <= max)
                return choice;
            Error($"Введите число от {min} до {max}");
        }
    }

    public static void PressAnyKey()
    {
        Console.WriteLine();
        Ln(ColDim, "  Нажмите любую клавишу...");
        Console.ReadKey(intercept: true);
    }

    // ─── Вспомогательные ──────────────────────────────────────────────────

    private static void W(ConsoleColor c, string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = c;
        Console.Write(text);
        Console.ForegroundColor = prev;
    }

    private static void Ln(ConsoleColor c, string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = c;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }

    private static string TruncateForDisplay(string s)
        => s.Length > 32 ? s[..12] + "…" + s[^10..] : s;
}
