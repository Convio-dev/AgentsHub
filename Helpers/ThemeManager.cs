using System.Drawing;

namespace HermesHub.Helpers;

public enum AppTheme
{
    Dark = 0,
    Light = 1
}

public class ThemeColors
{
    public required Color WindowBackground { get; init; }
    public required Color HeaderBackground { get; init; }
    public required Color FooterBackground { get; init; }
    public required Color CardBackground { get; init; }
    public required Color CardBorder { get; init; }
    public required Color WellBackground { get; init; }
    public required Color WellBorder { get; init; }

    public required Color TextPrimary { get; init; }
    public required Color TextSecondary { get; init; }
    public required Color TextMuted { get; init; }

    public required Color AccentGreen { get; init; }
    public required Color AccentGreenHover { get; init; }
    public required Color AccentGreenText { get; init; }

    public required Color AccentIndigo { get; init; }
    public required Color AccentIndigoHover { get; init; }

    public required Color ButtonBg { get; init; }
    public required Color ButtonHover { get; init; }
    public required Color ButtonBorder { get; init; }
    public required Color ButtonText { get; init; }

    public required Color DangerRed { get; init; }
    public required Color DangerRedHover { get; init; }
    public required Color WarningAmber { get; init; }

    public required Color ConsoleBg { get; init; }
    public required Color ConsoleText { get; init; }

    public required Color StatusDotOnline { get; init; }
    public required Color StatusDotOffline { get; init; }

    public required Color CheckBoxText { get; init; }
    public required Color HeaderButtonHover { get; init; }
}

public static class ThemeManager
{
    private static AppTheme _currentTheme = AppTheme.Dark;

    public static AppTheme CurrentTheme => _currentTheme;

    public static readonly ThemeColors Dark = new()
    {
        WindowBackground = Color.FromArgb(13, 14, 18),
        HeaderBackground = Color.FromArgb(19, 20, 26),
        FooterBackground = Color.FromArgb(19, 20, 26),
        CardBackground = Color.FromArgb(24, 25, 33),
        CardBorder = Color.FromArgb(38, 40, 52),
        WellBackground = Color.FromArgb(15, 16, 21),
        WellBorder = Color.FromArgb(38, 40, 52),

        TextPrimary = Color.FromArgb(248, 249, 250),
        TextSecondary = Color.FromArgb(160, 165, 180),
        TextMuted = Color.FromArgb(110, 115, 130),

        AccentGreen = Color.FromArgb(16, 185, 129),
        AccentGreenHover = Color.FromArgb(5, 150, 105),
        AccentGreenText = Color.FromArgb(10, 10, 12),

        AccentIndigo = Color.FromArgb(99, 102, 241),
        AccentIndigoHover = Color.FromArgb(79, 70, 229),

        ButtonBg = Color.FromArgb(32, 34, 44),
        ButtonHover = Color.FromArgb(45, 48, 62),
        ButtonBorder = Color.FromArgb(48, 52, 68),
        ButtonText = Color.FromArgb(240, 242, 248),

        DangerRed = Color.FromArgb(239, 68, 68),
        DangerRedHover = Color.FromArgb(220, 38, 38),
        WarningAmber = Color.FromArgb(245, 158, 11),

        ConsoleBg = Color.FromArgb(12, 13, 16),
        ConsoleText = Color.FromArgb(220, 225, 235),

        StatusDotOnline = Color.FromArgb(16, 185, 129),
        StatusDotOffline = Color.FromArgb(239, 68, 68),

        CheckBoxText = Color.FromArgb(220, 224, 235),
        HeaderButtonHover = Color.FromArgb(38, 40, 52)
    };

    public static readonly ThemeColors Light = new()
    {
        WindowBackground = Color.FromArgb(243, 244, 248),
        HeaderBackground = Color.FromArgb(255, 255, 255),
        FooterBackground = Color.FromArgb(255, 255, 255),
        CardBackground = Color.FromArgb(255, 255, 255),
        CardBorder = Color.FromArgb(222, 226, 236),
        WellBackground = Color.FromArgb(240, 242, 247),
        WellBorder = Color.FromArgb(215, 220, 230),

        TextPrimary = Color.FromArgb(15, 23, 42),
        TextSecondary = Color.FromArgb(71, 85, 105),
        TextMuted = Color.FromArgb(100, 116, 139),

        AccentGreen = Color.FromArgb(5, 150, 105),
        AccentGreenHover = Color.FromArgb(4, 120, 87),
        AccentGreenText = Color.FromArgb(255, 255, 255),

        AccentIndigo = Color.FromArgb(79, 70, 229),
        AccentIndigoHover = Color.FromArgb(67, 56, 202),

        ButtonBg = Color.FromArgb(241, 245, 249),
        ButtonHover = Color.FromArgb(226, 232, 240),
        ButtonBorder = Color.FromArgb(203, 213, 225),
        ButtonText = Color.FromArgb(15, 23, 42),

        DangerRed = Color.FromArgb(220, 38, 38),
        DangerRedHover = Color.FromArgb(185, 28, 28),
        WarningAmber = Color.FromArgb(217, 119, 6),

        ConsoleBg = Color.FromArgb(248, 250, 252),
        ConsoleText = Color.FromArgb(30, 41, 59),

        StatusDotOnline = Color.FromArgb(5, 150, 105),
        StatusDotOffline = Color.FromArgb(220, 38, 38),

        CheckBoxText = Color.FromArgb(30, 41, 59),
        HeaderButtonHover = Color.FromArgb(235, 238, 245)
    };

    public static ThemeColors Colors => _currentTheme == AppTheme.Light ? Light : Dark;

    public static void Initialize()
    {
        var savedTheme = StartupHelper.GetIntPreference("AppTheme", (int)AppTheme.Dark);
        _currentTheme = savedTheme == (int)AppTheme.Light ? AppTheme.Light : AppTheme.Dark;
    }

    public static void ToggleTheme()
    {
        _currentTheme = _currentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        StartupHelper.SetIntPreference("AppTheme", (int)_currentTheme);
    }

    public static void SetTheme(AppTheme theme)
    {
        _currentTheme = theme;
        StartupHelper.SetIntPreference("AppTheme", (int)_currentTheme);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void ApplyDwmTitleBarTheme(IntPtr windowHandle)
    {
        try
        {
            if (windowHandle == IntPtr.Zero) return;
            int useDark = _currentTheme == AppTheme.Dark ? 1 : 0;
            _ = DwmSetWindowAttribute(windowHandle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch
        {
            // Suppress on older Windows versions
        }
    }
}
