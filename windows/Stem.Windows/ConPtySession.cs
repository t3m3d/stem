using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Stem.Windows;

/// <summary>
/// Owns one Windows pseudoconsole and the shell attached to it. This file is
/// intentionally self-contained so the boundary can move back into Krypton
/// once the Windows native backend can emit the equivalent bindings.
/// </summary>
public sealed class ConPtySession : IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int StartfUseStdHandles = 0x00000100;
    private const uint ProcThreadAttributePseudoConsole = 0x00020016;

    private readonly SafeFileHandle _inputHandle;
    private readonly SafeFileHandle _outputHandle;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly IntPtr _pseudoConsole;
    private IntPtr _process;
    private IntPtr _thread;
    private int _disposed;
    private int _readStarted;

    public event Action<byte[]>? OutputReceived;
    public event Action? Exited;

    private ConPtySession(
        IntPtr pseudoConsole,
        IntPtr process,
        IntPtr thread,
        SafeFileHandle inputHandle,
        SafeFileHandle outputHandle)
    {
        _pseudoConsole = pseudoConsole;
        _process = process;
        _thread = thread;
        _inputHandle = inputHandle;
        _outputHandle = outputHandle;
    }

    public static ConPtySession Start(
        string commandLine,
        int columns,
        int rows,
        string? workingDirectory = null,
        string? terminalType = null)
    {
        EnsureWindowsVersion();
        var childWorkingDirectory = ResolveWorkingDirectory(workingDirectory);

        SafeFileHandle? inputRead = null;
        SafeFileHandle? inputWrite = null;
        SafeFileHandle? outputRead = null;
        SafeFileHandle? outputWrite = null;
        IntPtr pseudoConsole = IntPtr.Zero;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        PROCESS_INFORMATION processInfo = default;

        try
        {
            // Match Microsoft's MiniTerm sample: synchronous, non-inheritable
            // pipes. ConPTY owns the connection; the child does not inherit
            // these host-side handles.
            Check(CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0), "CreatePipe(input)");
            Check(CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0), "CreatePipe(output)");

            var size = new COORD(ClampDimension(columns), ClampDimension(rows));
            var hResult = CreatePseudoConsole(size, inputRead, outputWrite, 0, out pseudoConsole);
            if (hResult != 0)
            {
                Marshal.ThrowExceptionForHR(hResult);
            }

            var attributeBytes = IntPtr.Zero;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeBytes);
            if (attributeBytes == IntPtr.Zero)
            {
                throw LastWin32("InitializeProcThreadAttributeList(size)");
            }

            attributeList = Marshal.AllocHGlobal(attributeBytes);
            Check(
                InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeBytes),
                "InitializeProcThreadAttributeList");

            Check(
                UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    new IntPtr(ProcThreadAttributePseudoConsole),
                    pseudoConsole,
                    new IntPtr(IntPtr.Size),
                    IntPtr.Zero,
                    IntPtr.Zero),
                "UpdateProcThreadAttribute(PSEUDOCONSOLE)");

            var startup = new STARTUPINFOEX
            {
                StartupInfo = new STARTUPINFO
                {
                    cb = Marshal.SizeOf<STARTUPINFOEX>(),
                    dwFlags = StartfUseStdHandles,
                    hStdInput = IntPtr.Zero,
                    hStdOutput = IntPtr.Zero,
                    hStdError = IntPtr.Zero
                },
                lpAttributeList = attributeList
            };
            var mutableCommandLine = new StringBuilder(commandLine);
            environmentBlock = BuildEnvironmentBlock(terminalType);
            Check(
                CreateProcessW(
                    null,
                    mutableCommandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    environmentBlock,
                    childWorkingDirectory,
                    ref startup,
                    out processInfo),
                "CreateProcessW");

            // The pseudoconsole duplicates its pipe ends during process
            // attachment. Close our copies so broken-pipe shutdown is observable.
            inputRead.Dispose();
            inputRead = null;
            outputWrite.Dispose();
            outputWrite = null;

            var session = new ConPtySession(
                pseudoConsole,
                processInfo.hProcess,
                processInfo.hThread,
                inputWrite,
                outputRead);
            inputWrite = null;
            outputRead = null;
            pseudoConsole = IntPtr.Zero;
            processInfo = default;
            return session;
        }
        catch
        {
            CloseIfValid(ref processInfo.hThread);
            CloseIfValid(ref processInfo.hProcess);
            if (pseudoConsole != IntPtr.Zero)
            {
                ClosePseudoConsole(pseudoConsole);
            }
            throw;
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }
            if (environmentBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentBlock);
            }
            inputRead?.Dispose();
            inputWrite?.Dispose();
            outputRead?.Dispose();
            outputWrite?.Dispose();
        }
    }

    public void StartReading()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            Interlocked.Exchange(ref _readStarted, 1) != 0)
        {
            return;
        }

        BeginReadLoop();
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var payload = bytes.ToArray();
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                if (!WriteFile(_inputHandle, payload, payload.Length, out _, IntPtr.Zero) &&
                    Volatile.Read(ref _disposed) == 0)
                {
                    throw LastWin32("WriteFile(ConPTY input)");
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var hr = ResizePseudoConsole(
            _pseudoConsole,
            new COORD(ClampDimension(columns), ClampDimension(rows)));
        if (hr < 0)
        {
            // A resize can race shell shutdown. The next output read will
            // report the exit; a stale size is preferable to crashing UI.
            return;
        }
    }

    private void BeginReadLoop()
    {
        _ = Task.Run(() =>
        {
            var buffer = new byte[16 * 1024];
            try
            {
                while (Volatile.Read(ref _disposed) == 0)
                {
                    if (!ReadFile(_outputHandle, buffer, buffer.Length, out var read, IntPtr.Zero))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error is 6 or 109 or 232 || Volatile.Read(ref _disposed) != 0)
                        {
                            break;
                        }
                        throw new Win32Exception(error, "ReadFile(ConPTY output) failed");
                    }
                    if (read == 0)
                    {
                        break;
                    }

                    var owned = new byte[read];
                    Buffer.BlockCopy(buffer, 0, owned, 0, read);
                    OutputReceived?.Invoke(owned);
                }
            }
            catch (Exception ex) when (Volatile.Read(ref _disposed) == 0)
            {
                OutputReceived?.Invoke(Encoding.UTF8.GetBytes(
                    "\r\n\u001b[31m[ConPTY read failed: " + ex.Message + "]\u001b[0m\r\n"));
            }
            finally
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    Exited?.Invoke();
                }
            }
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _inputHandle.Dispose();
        ClosePseudoConsole(_pseudoConsole);
        _outputHandle.Dispose();
        CloseIfValid(ref _thread);
        CloseIfValid(ref _process);
    }

    private static short ClampDimension(int value) => (short)Math.Clamp(value, 2, short.MaxValue);

    private static IntPtr BuildEnvironmentBlock(string? terminalType)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                variables[key] = value;
            }
        }

        // Do not inherit TERM=dumb from launchers such as CI or IDE terminals.
        // ConPTY and STEM's renderer support xterm-compatible color sequences.
        variables.Remove("NO_COLOR");
        variables["CLICOLOR"] = "1";
        var term = string.IsNullOrWhiteSpace(terminalType) || terminalType.Contains('\0')
            ? "xterm-256color"
            : terminalType.Trim();
        variables["TERM"] = term;
        variables["COLORTERM"] = "truecolor";
        variables["TERM_PROGRAM"] = "stem";

        var block = string.Join('\0', variables.Select(pair => $"{pair.Key}={pair.Value}")) + '\0';
        return Marshal.StringToHGlobalUni(block);
    }

    private static string ResolveWorkingDirectory(string? configured)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.CurrentDirectory;
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            return home;
        }

        var value = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
        if (value == "~")
        {
            value = home;
        }
        else if (value.StartsWith("~/", StringComparison.Ordinal) ||
                 value.StartsWith("~\\", StringComparison.Ordinal))
        {
            value = Path.Combine(home, value[2..]);
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            return Directory.Exists(fullPath) ? fullPath : home;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return home;
        }
    }

    private static void EnsureWindowsVersion()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            throw new PlatformNotSupportedException("ConPTY requires Windows 10 version 1809 or newer.");
        }
    }

    private static void Check(bool ok, string operation)
    {
        if (!ok)
        {
            throw LastWin32(operation);
        }
    }

    private static Win32Exception LastWin32(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, $"{operation} failed: {new Win32Exception(error).Message}");
    }

    private static void CloseIfValid(ref IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            return;
        }

        _ = CloseHandle(handle);
        handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public COORD(short x, short y)
        {
            X = x;
            Y = y;
        }

        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        COORD size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateProcessW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        [In] ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(
        SafeFileHandle handle,
        byte[] buffer,
        int bytesToRead,
        out int bytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(
        SafeFileHandle handle,
        byte[] buffer,
        int bytesToWrite,
        out int bytesWritten,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}

public static class ShellCommand
{
    public static string Resolve(string? configured = null)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = Environment.GetEnvironmentVariable("STEM_SHELL");
        }
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var pwsh = FindOnPath("pwsh.exe");
        if (pwsh is not null)
        {
            return Quote(pwsh) + " -NoLogo";
        }

        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var windowsPowerShell = Path.Combine(
            systemRoot,
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (File.Exists(windowsPowerShell))
        {
            return Quote(windowsPowerShell) + " -NoLogo";
        }

        return Quote(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") + " /d";
    }

    public static string DisplayName(string commandLine)
    {
        if (commandLine.Contains("pwsh", StringComparison.OrdinalIgnoreCase))
        {
            return "PowerShell 7";
        }

        if (commandLine.Contains("powershell", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows PowerShell";
        }

        if (commandLine.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Command Prompt";
        }

        return "Custom shell";
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (var part in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(part.Trim('"'), fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
}
