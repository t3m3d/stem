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
            new(250, 248, 255), new(36, 25, 51), new(124, 58, 237),
            new(218, 204, 255), new(124, 58, 237), new(139, 92, 246),
            [
                new(43, 33, 58), new(198, 40, 78), new(8, 127, 91), new(148, 98, 0),
                new(37, 89, 179), new(143, 62, 151), new(11, 114, 133), new(111, 101, 125),
                new(129, 117, 143), new(220, 53, 94), new(10, 147, 105), new(168, 112, 0),
                new(57, 115, 209), new(166, 79, 175), new(20, 145, 168), new(33, 24, 46)
            ])
        : new KryptonTerminalPalette(
            new(5, 7, 12), new(216, 218, 212), new(139, 92, 246),
            new(58, 42, 96), new(139, 92, 246), new(139, 92, 246),
            StemSettings.DefaultAnsiPalette());

    public static void ApplyApplication(string? theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        var resources = Application.Current.Resources;
        var light = IsLight(theme);
        SetBrush(resources, "KryptonBackgroundBrush", light ? 0xF8F5FF : 0x10081D);
        SetBrush(resources, "KryptonPanelBrush", light ? 0xF0E9FF : 0x180C2A);
        SetBrush(resources, "KryptonShellBrush", light ? 0xE9DDFF : 0x260D46);
        SetBrush(resources, "KryptonTerminalChromeBrush", light ? 0xFFFFFF : 0x07050C);
        SetBrush(resources, "KryptonFieldBrush", light ? 0xFFFFFF : 0x0B0712);
        SetBrush(resources, "KryptonTextBrush", light ? 0x241933 : 0xF0EBFA);
        SetBrush(resources, "KryptonStrongTextBrush", light ? 0x170D26 : 0xFFFFFF);
        SetBrush(resources, "KryptonTitleTextBrush", 0xFFFFFF);
        SetBrush(resources, "KryptonMutedBrush", light ? 0x695D79 : 0xB4A8C8);
        SetBrush(resources, "KryptonSubtleBrush", light ? 0x8C819B : 0x746985);
        SetBrush(resources, "KryptonAccentBrush", light ? 0x7C3AED : 0x8B5CF6);
        SetBrush(resources, "KryptonAccentHighlightBrush", light ? 0x6D28D9 : 0xD7C3FF);
        SetBrush(resources, "KryptonBorderBrush", light ? 0xA78BFA : 0x654698);
        SetBrush(resources, "KryptonFrameBrush", light ? 0x7C3AED : 0xB79AFF);
        SetBrush(resources, "KryptonMenuBrush", light ? 0xFCFAFF : 0x160D25);
        SetBrush(resources, "KryptonMenuHoverBrush", light ? 0xE9DDFF : 0x3A1D64);
        SetBrush(resources, "KryptonMenuSelectedBrush", light ? 0xDAC8FF : 0x512B82);
        SetBrush(resources, "KryptonStatusBrush", light ? 0xEDE4FF : 0x170A29);
        SetBrush(resources, "KryptonChromeHoverBrush", light ? 0x30FFFFFF : 0x24FFFFFF);
        SetBrush(resources, "KryptonChromePressedBrush", light ? 0x48FFFFFF : 0x38FFFFFF);
        SetBrush(resources, "KryptonDangerBrush", 0xC63C51);
        SetBrush(resources, "KryptonWarningBrush", light ? 0x9A6500 : 0xE4B856);
        SetBrush(resources, "KryptonSelectionBrush", light ? 0xDAC8FF : 0x3A2A60);
        SetBrush(resources, "KryptonShadowBrush", light ? 0x553A235F : 0xAA000000, alphaIncluded: true);

        resources["KryptonShellStartColor"] = ColorValue(light ? 0x7C3AED : 0x4C1D95);
        resources["KryptonShellMidColor"] = ColorValue(light ? 0x8B5CF6 : 0x6D28D9);
        resources["KryptonShellEndColor"] = ColorValue(light ? 0xA78BFA : 0x2E1065);
        resources["KryptonFrameStartColor"] = ColorValue(light ? 0xA78BFA : 0xD7C3FF);
        resources["KryptonFrameEndColor"] = ColorValue(light ? 0x6D28D9 : 0x6D28D9);
        resources["KryptonWavePrimaryColor"] = ColorValue(light ? 0x72FFFFFF : 0x58FFFFFF, alphaIncluded: true);
        resources["KryptonWaveSecondaryColor"] = ColorValue(light ? 0x55E9DDFF : 0x48C4A7FF, alphaIncluded: true);
    }

    private static void SetBrush(ResourceDictionary resources, string key, uint value, bool alphaIncluded = false) =>
        resources[key] = new SolidColorBrush(ColorValue(value, alphaIncluded));

    private static Color ColorValue(uint value, bool alphaIncluded = false) => alphaIncluded
        ? Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
        : Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
}
