using Performa.Git;

namespace Performa.Reports;

public sealed record ReportGroup(string Title, IReadOnlyList<Commit> Commits);

public sealed record StandupFacts(
    string RepoName,
    DateTimeOffset Since,
    DateTimeOffset Now,
    IReadOnlyList<ReportGroup> Groups,
    int UnpushedCommits,
    int UncommittedFiles);

public sealed record ChangelogFacts(
    string Heading,
    string FromRef,
    string ToRef,
    IReadOnlyList<(string Section, IReadOnlyList<Commit> Commits)> Sections);

public sealed record SummaryFacts(
    string Target,
    string BaseRef,
    IReadOnlyList<ReportGroup> Groups,
    IReadOnlyList<string> Reasons,
    int Files,
    int Insertions,
    int Deletions);

public sealed record LooseEndsFacts(
    IReadOnlyList<BranchInfo> StaleBranches,
    IReadOnlyList<BranchInfo> UnpushedBranches,
    WorkingState Working,
    IReadOnlyList<TodoMarker> Todos,
    int TodoTotal);
