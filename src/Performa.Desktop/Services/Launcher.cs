using System.Diagnostics;

namespace Performa.Desktop.Services;

/// <summary>
/// Opening things outside Performa: a repo in an editor, a page in a browser.
///
/// Everything here goes through the shell rather than naming a program, so the
/// user's own file and browser associations decide what actually opens. The one
/// exception is the editor, which has no useful association for a folder.
/// </summary>
public static class Launcher
{
    /// <summary>
    /// The command to open a folder as a project, or null to fall back to the
    /// file manager.
    ///
    /// Detected rather than assumed: defaulting to <c>code</c> is wrong for
    /// anyone on JetBrains, and defaulting to JetBrains is wrong for everyone
    /// else. A configured preference always wins.
    /// </summary>
    public static string? Editor(string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred)) return preferred;
        foreach (var candidate in (string[])["idea64", "idea", "code", "subl"])
            if (OnPath(candidate)) return candidate;
        return null;
    }

    private static bool OnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null) return false;

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            foreach (var ext in (string[])[".exe", ".cmd", ".bat", ""])
            {
                try
                {
                    if (File.Exists(Path.Combine(dir, name + ext))) return true;
                }
                catch (ArgumentException) { }   // a malformed PATH entry is not fatal
            }
        }
        return false;
    }

    /// <summary>Opens a repository, in the editor if there is one and in the
    /// file manager if not. Never throws: failing to open a folder should not
    /// take the app down.</summary>
    public static void OpenRepo(string path, string? preferredEditor)
    {
        var editor = Editor(preferredEditor);
        try
        {
            if (editor is null)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }
            Process.Start(new ProcessStartInfo(editor, $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // A configured editor that is not really there, or a path that has
            // moved since the dashboard was drawn. Try the folder, then give up.
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception) { }
        }
    }

    public static void OpenUrl(string url)
    {
        // Only ever called with a URL Performa built from a git remote, but the
        // scheme is checked anyway rather than handing the shell whatever a
        // remote happened to contain.
        if (!url.StartsWith("https://", StringComparison.Ordinal)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }
}
