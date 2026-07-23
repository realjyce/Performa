using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Performa.Desktop.Services;

/// <summary>
/// Launch Performa when the desktop starts.
///
/// Uses the per-user Run key rather than a machine-wide entry or a scheduled
/// task: it needs no administrator rights, it is visible to the user in Task
/// Manager's Startup tab where they can disable it themselves, and removing it
/// is a single delete. The app is launched with --startup so it comes up
/// minimised instead of stealing focus at boot.
/// </summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Performa";
    public const string StartupArgument = "--startup";

    public static bool IsSupported => OperatingSystem.IsWindows();

    [SupportedOSPlatform("windows")]
    private static RegistryKey? OpenRunKey(bool writable)
        => Registry.CurrentUser.OpenSubKey(RunKeyPath, writable);

    public static bool IsEnabled()
    {
        // Called rather than IsSupported so the platform analyser can see the
        // guard and follow it into the registry calls below.
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = OpenRunKey(writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Turns boot launch on or off. Returns a human-readable outcome.</summary>
    public static string Set(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            return "Launching at startup is only wired up for Windows.";

        var exe = Environment.ProcessPath;
        if (enabled && (exe is null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            return "Run the published Performa.exe to set this up.";

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return "Could not open the startup settings.";

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{exe}\" {StartupArgument}");
                return "Performa will start with Windows.";
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return "Performa will no longer start with Windows.";
        }
        catch (UnauthorizedAccessException) { return "Windows refused the change."; }
        catch (Exception e) { return "Could not change it: " + e.Message; }
    }
}
