using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Stem.Windows;

internal static class DarkWindowTheme
{
    private const int ImmersiveDarkModeBefore20H1 = 19;
    private const int ImmersiveDarkMode = 20;

    public static void Apply(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            ApplyNow(window);
            return;
        }

        window.SourceInitialized += (_, _) => ApplyNow(window);
    }

    private static void ApplyNow(Window window)
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

        var enabled = 1;
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
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}
