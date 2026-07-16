using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Stem.Windows;

internal static class DarkWindowTheme
{
    private const int ImmersiveDarkModeBefore20H1 = 19;
    private const int ImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int SystemBackdropType = 38;
    private const int RoundCorner = 2;
    private const int MainWindowBackdrop = 2;

    public static void Apply(Window window, bool dark = true)
    {
        window.Background = Brushes.Transparent;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            ApplyNow(window, dark);
            return;
        }

        window.SourceInitialized += (_, _) => ApplyNow(window, dark);
    }

    private static void ApplyNow(Window window, bool dark)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var enabled = dark ? 1 : 0;
        if (DwmSetWindowAttribute(
                handle,
                ImmersiveDarkMode,
                ref enabled,
                Marshal.SizeOf<int>()) != 0)
        {
            _ = DwmSetWindowAttribute(
                handle,
                ImmersiveDarkModeBefore20H1,
                ref enabled,
                Marshal.SizeOf<int>());
        }

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var corner = RoundCorner;
            _ = DwmSetWindowAttribute(handle, WindowCornerPreference, ref corner, Marshal.SizeOf<int>());
            var backdrop = MainWindowBackdrop;
            _ = DwmSetWindowAttribute(handle, SystemBackdropType, ref backdrop, Marshal.SizeOf<int>());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
}
