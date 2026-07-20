using System.Diagnostics;
using System.Text;

namespace Recap.Git;

public sealed class GitException(string message) : Exception(message);

public sealed class GitRunner(string repoPath)
{
    public string RepoPath { get; } = Path.GetFullPath(repoPath);

    public bool IsRepository()
        => TryRun("rev-parse", "--is-inside-work-tree")?.Trim() == "true";

    public string Run(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)
            ?? throw new GitException("Failed to start git. Is git installed and on PATH?");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new GitException(
                $"git {string.Join(' ', args)} failed ({process.ExitCode}): {stderr.Trim()}");
        return stdout;
    }

    public string? TryRun(params string[] args)
    {
        try { return Run(args); }
        catch (GitException) { return null; }
    }
}
