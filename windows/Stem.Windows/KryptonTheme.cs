using System.Windows;
using System.Windows.Media;

namespace Stem.Windows;

public sealed record KryptonTerminalPalette(
    TerminalColor Background,
    TerminalColor Foreground,
    TerminalColor Accent,
    TerminalColor Selection,
    TerminalColor Cursor,
    TerminalColor SplitDivider,
    TerminalColor[] Ansi);

public static class KryptonTheme
{
    public const string Dark = "krypton-dark";
    public const string Light = "krypton-light";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" or "krypton-light" => Light,
        _ => Dark
    };

    public static bool IsLight(string? value) => Normalize(value) == Light;

    public static KryptonTerminalPalette TerminalPalette(string? theme) => IsLight(theme)
        ? new KryptonTerminalPalette(
            new(238, 230, 255), new(39, 20, 61), new(124, 58, 237),
            new(211, 193, 255), new(124, 58, 237), new(139, 92, 246),
            [
                new(43, 26, 64), new(185, 35, 73), new(5, 120, 84), new(143, 91, 0),
                new(35, 82, 171), new(131, 51, 143), new(4, 105, 126), new(101, 87, 119),
                new(120, 102, 140), new(211, 43, 85), new(8, 143, 99), new(161, 105, 0),
                new(52, 105, 199), new(155, 65, 166), new(15, 134, 154), new(39, 20, 61)
            ])
        : new KryptonTerminalPalette(
            new(22, 10, 42), new(244, 238, 255), new(139, 92, 246),
            new(61, 40, 107), new(139, 92, 246), new(139, 92, 246),
            StemSettings.DefaultAnsiPalette());


    public static void ApplyApplication(string? theme, double backgroundOpacity = 1)
    {
        if (Application.Current is null)
        {
            return;
        }

        var resources = Application.Current.Resources;
        var light = IsLight(theme);
        var opacity = Math.Clamp(backgroundOpacity, 0.2, 1);
        SetBrush(resources, "KryptonBackgroundBrush", light ? 0xF8F5FF : 0x10081D, opacity);
        SetBrush(resources, "KryptonPanelBrush", light ? 0xF0E9FF : 0x180C2A, opacity);
        SetBrush(resources, "KryptonShellBrush", light ? 0xE9DDFF : 0x260D46, opacity);
        SetBrush(resources, "KryptonTerminalChromeBrush", light ? 0xEEE6FF : 0x160A2A, opacity);
        SetBrush(resources, "KryptonFieldBrush", light ? 0xFFFFFF : 0x0B0712, Math.Max(0.72, opacity));
        SetBrush(resources, "KryptonTextBrush", light ? 0x241933 : 0xF0EBFA);
        SetBrush(resources, "KryptonStrongTextBrush", light ? 0x170D26 : 0xFFFFFF);
        SetBrush(resources, "KryptonTitleTextBrush", 0xFFFFFF);
        SetBrush(resources, "KryptonMutedBrush", light ? 0x695D79 : 0xB4A8C8);
        SetBrush(resources, "KryptonSubtleBrush", light ? 0x8C819B : 0x746985);
        SetBrush(resources, "KryptonAccentBrush", light ? 0x7C3AED : 0x8B5CF6);
        SetBrush(resources, "KryptonAccentHighlightBrush", light ? 0x6D28D9 : 0xD7C3FF);
        SetBrush(resources, "KryptonBorderBrush", light ? 0xA78BFA : 0x654698);
        SetBrush(resources, "KryptonFrameBrush", light ? 0x7C3AED : 0xB79AFF);
        SetBrush(resources, "KryptonMenuBrush", light ? 0xFCFAFF : 0x160D25, Math.Max(0.9, opacity));
        SetBrush(resources, "KryptonMenuHoverBrush", light ? 0xE9DDFF : 0x3A1D64);
        SetBrush(resources, "KryptonMenuSelectedBrush", light ? 0xDAC8FF : 0x512B82);
        SetBrush(resources, "KryptonStatusBrush", light ? 0xEDE4FF : 0x170A29, opacity);
        SetBrush(resources, "KryptonChromeHoverBrush", light ? 0x30FFFFFF : 0x24FFFFFF);
        SetBrush(resources, "KryptonChromePressedBrush", light ? 0x48FFFFFF : 0x38FFFFFF);
        SetBrush(resources, "KryptonDangerBrush", 0xC63C51);
        SetBrush(resources, "KryptonWarningBrush", light ? 0x9A6500 : 0xE4B856);
        SetBrush(resources, "KryptonSelectionBrush", light ? 0xDAC8FF : 0x3A2A60);
        SetBrush(resources, "KryptonShadowBrush", light ? 0x553A235F : 0xAA000000, alphaIncluded: true);

        resources["KryptonShellStartColor"] = ColorValue(light ? 0x7C3AED : 0x4C1D95, opacity);
        resources["KryptonShellMidColor"] = ColorValue(light ? 0x8B5CF6 : 0x6D28D9, opacity);
        resources["KryptonShellEndColor"] = ColorValue(light ? 0xA78BFA : 0x2E1065, opacity);
        resources["KryptonFrameStartColor"] = ColorValue(light ? 0xA78BFA : 0xD7C3FF);
        resources["KryptonFrameEndColor"] = ColorValue(light ? 0x6D28D9 : 0x6D28D9);
        resources["KryptonWavePrimaryColor"] = ColorValue(light ? 0x72FFFFFF : 0x58FFFFFF, alphaIncluded: true);
        resources["KryptonWaveSecondaryColor"] = ColorValue(light ? 0x55E9DDFF : 0x48C4A7FF, alphaIncluded: true);
        SetBrush(resources, "KryptonWavePrimaryBrush", light ? 0x72FFFFFF : 0x58FFFFFF, alphaIncluded: true);
        SetBrush(resources, "KryptonWaveSecondaryBrush", light ? 0x55E9DDFF : 0x48C4A7FF, alphaIncluded: true);
    }

    private static void SetBrush(ResourceDictionary resources, string key, long value, double opacity = 1) =>
        resources[key] = new SolidColorBrush(ColorValue(value, opacity));

    private static void SetBrush(ResourceDictionary resources, string key, long value, bool alphaIncluded) =>
        resources[key] = new SolidColorBrush(ColorValue(value, alphaIncluded));

    private static Color ColorValue(long value, double opacity) =>
        Color.FromArgb(
            (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value);

    private static Color ColorValue(long value, bool alphaIncluded = false) => alphaIncluded
        ? Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
        : Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
}
