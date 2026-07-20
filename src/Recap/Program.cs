using System.CommandLine;
using Recap.Enrich;
using Recap.Git;
using Recap.Prefs;
using Recap.Reports;

var repoOption = new Option<string>("--repo")
{
    Description = "Path to the git repository (default: current directory)",
    DefaultValueFactory = _ => ".",
    Recursive = true,
};
var formatOption = new Option<string?>("--format")
{
    Description = "Override output format for this run: md or text",
    Recursive = true,
};
var noPromptOption = new Option<bool>("--no-prompt")
{
    Description = "Skip the accept/edit/reject prompt (also skipped when piped)",
    Recursive = true,
};

var root = new RootCommand(
    "recap: turns your local git history into standups, changelogs, branch summaries, and loose-end reports. No network, no AI, just your commits.");
root.Options.Add(repoOption);
root.Options.Add(formatOption);
root.Options.Add(noPromptOption);

var initCommand = new Command("init", "Answer a few questions to set your output preferences.");
initCommand.SetAction(parseResult =>
{
    var store = new PrefsStore();
    var prefs = store.LoadPrefs();
    RunInit(prefs);
    store.SavePrefs(prefs);
    Console.Error.WriteLine("recap: preferences saved.");
    return 0;
});
root.Subcommands.Add(initCommand);

var sinceOption = new Option<string?>("--since")
{
    Description = "Start point: a date (2026-07-18), 'yesterday', or any git ref. Default: your last standup.",
};
var standupCommand = new Command("standup", "What you did since a given point, grouped sensibly.");
standupCommand.Options.Add(sinceOption);
standupCommand.SetAction(parseResult => WithRepo(parseResult, (repo, prefs, store, enricher, noPrompt) =>
{
    var state = store.LoadState();
    var now = DateTimeOffset.Now;
    var since = ResolveSince(parseResult.GetValue(sinceOption), repo, state);
    var facts = FactBuilders.BuildStandup(repo, prefs, since, now);
    var output = enricher.RenderStandup(facts, prefs);
    var (text, action) = Feedback.Collect(output, prefs, store, noPrompt);
    Console.Write(text);
    if (action != FeedbackAction.Reject)
    {
        state.LastStandup[repo.Git.RepoPath] = now;
        store.SaveState(state);
    }
}));
root.Subcommands.Add(standupCommand);

var fromOption = new Option<string?>("--from")
{
    Description = "Start ref/tag. Default: the most recent tag (or full history if none).",
};
var toOption = new Option<string>("--to")
{
    Description = "End ref. Default: HEAD.",
    DefaultValueFactory = _ => "HEAD",
};
var changelogCommand = new Command("changelog", "Release notes generated from commits.");
changelogCommand.Options.Add(fromOption);
changelogCommand.Options.Add(toOption);
changelogCommand.SetAction(parseResult => WithRepo(parseResult, (repo, prefs, store, enricher, noPrompt) =>
{
    var facts = FactBuilders.BuildChangelog(
        repo, parseResult.GetValue(fromOption), parseResult.GetValue(toOption)!);
    var (text, _) = Feedback.Collect(enricher.RenderChangelog(facts, prefs), prefs, store, noPrompt);
    Console.Write(text);
}));
root.Subcommands.Add(changelogCommand);

var targetArgument = new Argument<string>("target")
{
    Description = "A branch name or an explicit range like v1.0..HEAD",
};
var summaryCommand = new Command("summary", "What changed and why, for a branch or range.");
summaryCommand.Arguments.Add(targetArgument);
summaryCommand.SetAction(parseResult => WithRepo(parseResult, (repo, prefs, store, enricher, noPrompt) =>
{
    var facts = FactBuilders.BuildSummary(repo, prefs, parseResult.GetValue(targetArgument)!);
    var (text, _) = Feedback.Collect(enricher.RenderSummary(facts, prefs), prefs, store, noPrompt);
    Console.Write(text);
}));
root.Subcommands.Add(summaryCommand);

var looseEndsCommand = new Command("loose-ends", "Stale branches, unpushed commits, uncommitted work, TODO/FIXME markers.");
looseEndsCommand.SetAction(parseResult => WithRepo(parseResult, (repo, prefs, _, enricher, _) =>
{
    var facts = FactBuilders.BuildLooseEnds(repo);
    Console.Write(enricher.RenderLooseEnds(facts, prefs));
}));
root.Subcommands.Add(looseEndsCommand);

return root.Parse(args).Invoke();

int WithRepo(ParseResult parseResult,
    Action<RepoQueries, Preferences, PrefsStore, IEnricher, bool> run)
{
    var repoPath = parseResult.GetValue(repoOption)!;
    var git = new GitRunner(repoPath);
    if (!Directory.Exists(git.RepoPath))
    {
        Console.Error.WriteLine($"recap: directory not found: {git.RepoPath}");
        return 2;
    }
    if (!git.IsRepository())
    {
        Console.Error.WriteLine($"recap: not a git repository: {git.RepoPath}");
        return 2;
    }

    var store = new PrefsStore();
    var prefs = store.LoadPrefs();
    if (!prefs.Initialised)
        Console.Error.WriteLine("recap: using defaults. Run `recap init` once to set preferences.");

    switch (parseResult.GetValue(formatOption)?.ToLowerInvariant())
    {
        case "md" or "markdown": prefs.Format = OutputFormat.Markdown; break;
        case "text" or "txt": prefs.Format = OutputFormat.Text; break;
    }

    try
    {
        run(new RepoQueries(git), prefs, store, new DeterministicEnricher(),
            parseResult.GetValue(noPromptOption));
        return 0;
    }
    catch (GitException e)
    {
        Console.Error.WriteLine($"recap: {e.Message}");
        return 1;
    }
}

DateTimeOffset ResolveSince(string? since, RepoQueries repo, StateFile state)
{
    if (since is null)
    {
        return state.LastStandup.TryGetValue(repo.Git.RepoPath, out var last)
            ? last
            : DateTimeOffset.Now.Date.AddDays(-1);
    }
    if (since.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
        return DateTimeOffset.Now.Date.AddDays(-1);
    if (DateTimeOffset.TryParse(since, out var parsed))
        return parsed;

    var refDate = repo.Git.TryRun("show", "-s", "--format=%aI", since)?.Trim();
    if (refDate is not null && DateTimeOffset.TryParse(refDate.Split('\n')[^1], out var fromRef))
        return fromRef;
    throw new GitException($"could not resolve --since '{since}' as a date or git ref");
}

void RunInit(Preferences prefs)
{
    Console.Error.WriteLine("recap setup. Enter to keep the [default].");
    prefs.Format = Ask("Output format", ("md", OutputFormat.Markdown), ("text", OutputFormat.Text));
    prefs.Verbosity = Ask("Verbosity", ("normal", Verbosity.Normal), ("terse", Verbosity.Terse), ("detailed", Verbosity.Detailed));
    prefs.Grouping = Ask("Group commits by", ("area", Grouping.Area), ("kind", Grouping.Kind), ("flat", Grouping.Flat));
    prefs.Tone = Ask("Tone", ("plain", Tone.Plain), ("friendly", Tone.Friendly));
    prefs.Initialised = true;
}

static T Ask<T>(string label, params (string Key, T Value)[] choices)
{
    var keys = string.Join("/", choices.Select((c, i) => i == 0 ? $"[{c.Key}]" : c.Key));
    Console.Error.Write($"{label} ({keys}): ");
    var answer = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
    foreach (var (key, value) in choices)
        if (key.StartsWith(answer, StringComparison.OrdinalIgnoreCase) && answer.Length > 0)
            return value;
    return choices[0].Value;
}
