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

public static partial class ThemeManager
{
    private static AppTheme _currentTheme = AppTheme.Dark;
    private static string _accentHex = "#4049B9";

    public static AppTheme CurrentTheme => _currentTheme;
    public static string AccentHex => _accentHex;

    public static (double h, double s, double l) HexToHsl(string hex)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length == 3)
        {
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        }
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out _))
        {
            return (236, 49, 49); // Default #4049B9
        }

        double r = Convert.ToInt32(hex[..2], 16) / 255.0;
        double g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0;
        double b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double h = 0, s = 0, l = (max + min) / 2.0;

        if (Math.Abs(max - min) > 0.0001)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (Math.Abs(max - r) < 0.0001)
                h = (g - b) / d + (g < b ? 6 : 0);
            else if (Math.Abs(max - g) < 0.0001)
                h = (b - r) / d + 2;
            else
                h = (r - g) / d + 4;
            h = Math.Round(h * 60.0);
        }
        s = Math.Round(s * 100.0);
        l = Math.Round(l * 100.0);
        return (h, s, l);
    }

    public static Color HslToColor(double h, double s, double l)
    {
        s = Math.Clamp(s / 100.0, 0.0, 1.0);
        l = Math.Clamp(l / 100.0, 0.0, 1.0);

        double k(double n) => (n + h / 30.0) % 12.0;
        double a = s * Math.Min(l, 1.0 - l);
        double f(double n) => l - a * Math.Max(-1.0, Math.Min(k(n) - 3.0, Math.Min(9.0 - k(n), 1.0)));

        int r = (int)Math.Round(Math.Clamp(f(0) * 255.0, 0, 255));
        int g = (int)Math.Round(Math.Clamp(f(8) * 255.0, 0, 255));
        int b = (int)Math.Round(Math.Clamp(f(4) * 255.0, 0, 255));
        return Color.FromArgb(r, g, b);
    }

    public static double GetLuminance(string hex)
    {
        hex = (hex ?? "").Trim().TrimStart('#');
        if (hex.Length == 3) hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out _)) return 0.5;
        double r = Convert.ToInt32(hex[..2], 16);
        double g = Convert.ToInt32(hex.Substring(2, 2), 16);
        double b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
    }

    public static ThemeColors Colors => GetAdaptiveColors();

    public static ThemeColors GetAdaptiveColors()
    {
        var (h, s, l) = HexToHsl(_accentHex);
        double lum = GetLuminance(_accentHex);

        if (_currentTheme == AppTheme.Light)
        {
            double sTintLight = s < 12 ? Math.Max(2, Math.Round(s * 0.4)) : Math.Min(24, Math.Max(4, Math.Round(s * 0.22)));
            Color accentColor = l > 48 ? HslToColor(h, Math.Max(s, 60), 42) : HslToColor(h, s, l);
            Color accentHover = HslToColor(h, s, 34);
            Color accentText = lum > 0.62 ? Color.FromArgb(10, 15, 20) : Color.White;

            return new ThemeColors
            {
                WindowBackground = HslToColor(h, sTintLight, 95.5),
                HeaderBackground = HslToColor(h, Math.Round(sTintLight * 1.5), 88), // Lavender header
                FooterBackground = HslToColor(h, Math.Round(sTintLight * 1.5), 88),
                CardBackground = Color.White,
                CardBorder = HslToColor(h, Math.Round(sTintLight * 1.25), 83),
                WellBackground = HslToColor(h, sTintLight, 96.8),
                WellBorder = HslToColor(h, Math.Round(sTintLight * 1.25), 83),

                TextPrimary = HslToColor(h, 28, 11),
                TextSecondary = HslToColor(h, 16, 36),
                TextMuted = HslToColor(h, 10, 52),

                AccentGreen = accentColor,
                AccentGreenHover = accentHover,
                AccentGreenText = accentText,

                AccentIndigo = HslToColor(h, Math.Max(s, 60), 42),
                AccentIndigoHover = HslToColor(h, s, 34),

                ButtonBg = Color.White,
                ButtonHover = HslToColor(h, sTintLight, 96.5),
                ButtonBorder = HslToColor(h, Math.Round(sTintLight * 1.25), 83),
                ButtonText = HslToColor(h, 28, 11),

                DangerRed = Color.FromArgb(220, 38, 38),
                DangerRedHover = Color.FromArgb(185, 28, 28),
                WarningAmber = Color.FromArgb(217, 119, 6),

                ConsoleBg = HslToColor(h, 15, 8),
                ConsoleText = Color.FromArgb(240, 242, 245),

                StatusDotOnline = accentColor,
                StatusDotOffline = Color.FromArgb(220, 38, 38),

                CheckBoxText = HslToColor(h, 28, 11),
                HeaderButtonHover = HslToColor(h, Math.Round(sTintLight * 1.5), 92)
            };
        }
        else
        {
            double sTint = s < 12 ? Math.Max(3, s) : Math.Min(32, Math.Max(6, Math.Round(s * 0.28)));
            Color accentColor = l < 42 ? HslToColor(h, Math.Max(s, 50), 52) : HslToColor(h, s, l);
            Color accentHover = HslToColor(h, s, Math.Max(12, l - 8));
            Color accentText = lum > 0.62 ? Color.FromArgb(10, 15, 20) : Color.White;

            return new ThemeColors
            {
                WindowBackground = HslToColor(h, sTint, 5.5),
                HeaderBackground = HslToColor(h, sTint + 2, 8.5),
                FooterBackground = HslToColor(h, sTint + 2, 8.5),
                CardBackground = HslToColor(h, sTint + 4, 12.5),
                CardBorder = HslToColor(h, sTint + 2, 19),
                WellBackground = HslToColor(h, sTint, 7),
                WellBorder = HslToColor(h, sTint + 2, 19),

                TextPrimary = Color.FromArgb(248, 250, 252),
                TextSecondary = HslToColor(h, 12, 68),
                TextMuted = HslToColor(h, 10, 48),

                AccentGreen = accentColor,
                AccentGreenHover = accentHover,
                AccentGreenText = accentText,

                AccentIndigo = accentColor,
                AccentIndigoHover = accentHover,

                ButtonBg = HslToColor(h, sTint + 4, 12.5),
                ButtonHover = HslToColor(h, sTint + 5, 16.5),
                ButtonBorder = HslToColor(h, sTint + 2, 19),
                ButtonText = Color.FromArgb(240, 242, 248),

                DangerRed = Color.FromArgb(239, 68, 68),
                DangerRedHover = Color.FromArgb(220, 38, 38),
                WarningAmber = Color.FromArgb(245, 158, 11),

                ConsoleBg = HslToColor(h, sTint, 5),
                ConsoleText = Color.FromArgb(240, 242, 248),

                StatusDotOnline = accentColor,
                StatusDotOffline = Color.FromArgb(239, 68, 68),

                CheckBoxText = Color.FromArgb(220, 224, 235),
                HeaderButtonHover = HslToColor(h, sTint + 4, 14)
            };
        }
    }

    public static event Action? ThemeChanged;

    public static void Initialize()
    {
        var savedTheme = StartupHelper.GetStringPreference("Theme", "dark");
        _currentTheme = savedTheme.Equals("light", StringComparison.OrdinalIgnoreCase) ? AppTheme.Light : AppTheme.Dark;
        _accentHex = StartupHelper.GetStringPreference("AccentColor", "#4049B9");
    }

    public static void ToggleTheme()
    {
        _currentTheme = _currentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        StartupHelper.SetStringPreference("Theme", _currentTheme == AppTheme.Light ? "light" : "dark");
        ThemeChanged?.Invoke();
    }

    public static void SetTheme(AppTheme theme, string? accentColor = null)
    {
        _currentTheme = theme;
        if (!string.IsNullOrWhiteSpace(accentColor))
        {
            _accentHex = accentColor;
            StartupHelper.SetStringPreference("AccentColor", _accentHex);
        }
        StartupHelper.SetStringPreference("Theme", _currentTheme == AppTheme.Light ? "light" : "dark");
        ThemeChanged?.Invoke();
    }

    [System.Runtime.InteropServices.LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

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
