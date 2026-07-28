using System.Diagnostics;
using System.Text;

namespace Performa.Desktop.Services;

/// <summary>
/// Windows toast notifications without a package dependency. A hidden
/// PowerShell process drives the WinRT toast API; clunky next to a proper COM
/// activator, but it needs no packaging, no TFM change, and no registration.
/// The toast informs - clicking it does not deep-link. Fire-and-forget: a
/// notification that fails is a notification missed, never a crash.
/// </summary>
public static class ToastService
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    public static void Show(string title, string message)
    {
        if (!IsSupported) return;
        try
        {
            // Values travel base64-encoded so no quoting in title or message
            // can escape the script.
            var t = Convert.ToBase64String(Encoding.Unicode.GetBytes(title));
            var m = Convert.ToBase64String(Encoding.Unicode.GetBytes(message));

            var script =
                "$t=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + t + "'));" +
                "$m=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('" + m + "'));" +
                "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType=WindowsRuntime] | Out-Null;" +
                "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType=WindowsRuntime] | Out-Null;" +
                "$x=[Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02);" +
                "$s=$x.GetElementsByTagName('text');" +
                "$s.Item(0).AppendChild($x.CreateTextNode($t))|Out-Null;" +
                "$s.Item(1).AppendChild($x.CreateTextNode($m))|Out-Null;" +
                "$toast=[Windows.UI.Notifications.ToastNotification]::new($x);" +
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Performa').Show($toast)";

            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" +
                            script.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process.Start(psi);
        }
        catch (Exception) { /* informing failed; the app carries on */ }
    }
}
