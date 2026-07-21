using System.Diagnostics;

namespace Performa.Prefs;

public static class Feedback
{
    public static (string Text, FeedbackAction Action) Collect(
        string output, Preferences prefs, PrefsStore store, bool noPrompt)
    {
        if (noPrompt || Console.IsInputRedirected || Console.IsOutputRedirected)
            return (output, FeedbackAction.Accept);

        Console.Error.Write("[a]ccept  [e]dit  [r]eject > ");
        var key = Console.ReadKey(intercept: true);
        Console.Error.WriteLine();

        switch (char.ToLowerInvariant(key.KeyChar))
        {
            case 'e':
                var edited = EditInEditor(output);
                var note = Adaptation.Apply(
                    prefs, FeedbackAction.Edit, output.Length, edited.Length);
                store.SavePrefs(prefs);
                if (note is not null) Console.Error.WriteLine($"performa: {note}");
                return (edited, FeedbackAction.Edit);

            case 'r':
                var rejectNote = Adaptation.Apply(prefs, FeedbackAction.Reject);
                store.SavePrefs(prefs);
                Console.Error.WriteLine(rejectNote is not null
                    ? $"performa: {rejectNote} Run the command again to regenerate."
                    : "performa: noted.");
                return (output, FeedbackAction.Reject);

            default:
                Adaptation.Apply(prefs, FeedbackAction.Accept);
                store.SavePrefs(prefs);
                return (output, FeedbackAction.Accept);
        }
    }

    private static string EditInEditor(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"performa-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, content);
        var editor = Environment.GetEnvironmentVariable("EDITOR")
            ?? (OperatingSystem.IsWindows() ? "notepad" : "nano");
        try
        {
            var process = Process.Start(new ProcessStartInfo(editor, path) { UseShellExecute = false });
            process?.WaitForExit();
            return File.ReadAllText(path);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
