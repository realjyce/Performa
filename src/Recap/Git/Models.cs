namespace Recap.Git;

public sealed record Commit(
    string Sha,
    string Author,
    string Email,
    DateTimeOffset Date,
    string Subject,
    string Body,
    IReadOnlyList<string> Files);

public sealed record BranchInfo(
    string Name,
    DateTimeOffset LastCommit,
    string? Upstream,
    int Ahead);

public sealed record WorkingState(int Staged, int Unstaged, int Untracked)
{
    public int Total => Staged + Unstaged + Untracked;
}

public sealed record TodoMarker(string File, int Line, string Kind, string Text);
