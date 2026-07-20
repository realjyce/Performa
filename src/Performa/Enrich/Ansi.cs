using System.Runtime.InteropServices;

namespace Performa.Enrich;

public static class Ansi
{
    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";
    public const string Cyan = "\x1b[36m";
    public const string Green = "\x1b[32m";
    public const string Yellow = "\x1b[33m";
    public const string Red = "\x1b[31m";
    public const string Blue = "\x1b[34m";
    public const string Magenta = "\x1b[35m";

    public static bool TerminalSupportsStyling()
    {
        if (Console.IsOutputRedirected) return false;
        if (Environment.GetEnvironmentVariable("NO_COLOR") is { Length: > 0 }) return false;
        if (OperatingSystem.IsWindows()) EnableVirtualTerminal();
        return true;
    }

    private static void EnableVirtualTerminal()
    {
        const int StdOutputHandle = -11;
        const uint EnableVtp = 0x0004;
        var handle = GetStdHandle(StdOutputHandle);
        if (GetConsoleMode(handle, out var mode))
            SetConsoleMode(handle, mode | EnableVtp);
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
