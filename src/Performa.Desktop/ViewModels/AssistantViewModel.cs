using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed class ChatMessage(bool isUser, string text, string? source = null)
{
    public bool IsUser { get; } = isUser;
    public bool IsAssistant => !IsUser;
    public string Text { get; } = text;

    /// <summary>What produced this answer. Shown so a model's prose is never
    /// mistaken for the deterministic reading of your git history.</summary>
    public string? Source { get; } = source;
    public bool HasSource => !string.IsNullOrEmpty(Source);
}

/// <summary>
/// Deterministic assistant. Answers from git facts today; the same ask/answer
/// shape sits behind the enrichment seam an AI model replaces later.
/// </summary>
public sealed class AssistantViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;
    private readonly AiService _ai = new();

    public AssistantViewModel(PerformaEngine engine)
    {
        _engine = engine;
        SendCommand = new RelayCommand(() => Ask(Input));
        AskSuggestionCommand = new RelayCommand<string>(s => { if (s is not null) Ask(s); });
        Messages.Add(new ChatMessage(false,
            "Ask me about your work. I read your git history and answer from facts, no guessing. Try a suggestion below."));
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public string[] Suggestions { get; } =
        ["What did I ship today?", "What's left?", "How's my week?"];

    private string _input = "";
    public string Input { get => _input; set => SetProperty(ref _input, value); }

    private bool _thinking;
    public bool Thinking { get => _thinking; set => SetProperty(ref _thinking, value); }

    public RelayCommand SendCommand { get; }
    public RelayCommand<string> AskSuggestionCommand { get; }

    private void Ask(string question)
    {
        question = question.Trim();
        if (question.Length == 0) return;
        Messages.Add(new ChatMessage(true, question));
        Input = "";
        _ = AnswerAsync(question);
    }

    private async Task AnswerAsync(string question)
    {
        Thinking = true;

        // Deterministic facts first: they are the ground truth either way.
        var facts = await Task.Run(() => Answer(question.ToLowerInvariant()));

        var key = AppCredentialStore.AiKey(_engine.Prefs, _engine.Prefs.AiProvider);
        if (_engine.Prefs.AiEnabled && !string.IsNullOrWhiteSpace(key))
        {
            var context = await Task.Run(() => BuildContext(question));
            var answer = await _ai.AskAsync(_engine.Prefs, context, question);
            if (answer is not null)
            {
                Messages.Add(new ChatMessage(false, answer.Text, answer.Model));
                ActiveModel = answer.Model;
                Thinking = false;
                return;
            }
            // The model was asked and did not answer, so say so rather than
            // letting the deterministic reply pass for a working AI.
            ActiveModel = "unavailable, answering from facts";
        }

        Messages.Add(new ChatMessage(false, facts, "your git history"));
        Thinking = false;
    }

    private string _activeModel = "";
    public string ActiveModel
    {
        get => _activeModel;
        set { if (SetProperty(ref _activeModel, value)) OnPropertyChanged(nameof(HasActiveModel)); }
    }

    public bool HasActiveModel => _activeModel.Length > 0;

    /// <summary>Which slice of the workspace a question actually needs.</summary>
    [Flags]
    public enum Facet
    {
        None = 0,
        Commits = 1,
        Velocity = 2,
        Repos = 4,
    }

    /// <summary>Reads the question for what it is asking about. A streak
    /// question does not need the commit list, and a "what's left" question
    /// does not need the streak. Sending everything every time spends tokens
    /// on facts the model has to read past to reach the one that matters, and
    /// the noise costs accuracy as well as money.</summary>
    public static Facet FacetsFor(string q)
    {
        var facets = Facet.None;
        if (Contains(q, "today", "ship", "shipped", "done", "did i", "commit"))
            facets |= Facet.Commits;
        if (Contains(q, "week", "velocity", "streak", "pace", "productive"))
            facets |= Facet.Velocity;
        if (Contains(q, "left", "loose", "unfinished", "todo", "pending", "cleanup", "branch", "push"))
            facets |= Facet.Repos;

        // An unrecognised question still needs somewhere to stand. Velocity is
        // the cheapest orientation: three numbers, no lists.
        return facets == Facet.None ? Facet.Velocity : facets;
    }

    /// <summary>Commits carried into context. Past this the model is reading a
    /// log rather than answering, and the tail is the least relevant part.</summary>
    private const int CommitBudget = 12;

    /// <summary>Repos carried into context, worst-first.</summary>
    private const int RepoBudget = 8;

    /// <summary>Only real git facts go to the model; it is never asked to invent.</summary>
    private string BuildContext(string question)
    {
        var facets = FacetsFor(question.ToLowerInvariant());
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are Performa, a developer's assistant. Facts about their work:");

        if (facets.HasFlag(Facet.Commits))
        {
            var today = _engine.TodayCommits();
            sb.AppendLine($"Commits today: {today.Count}");
            foreach (var (repo, when, subject) in today.Take(CommitBudget))
                sb.AppendLine($"- {when:HH:mm} [{repo}] {subject}");
            if (today.Count > CommitBudget)
                sb.AppendLine($"- (+{today.Count - CommitBudget} older ones today, not listed)");
        }

        if (facets.HasFlag(Facet.Velocity) || facets.HasFlag(Facet.Repos))
        {
            try
            {
                var facts = _engine.BuildWorkspace();

                if (facets.HasFlag(Facet.Velocity))
                {
                    var v = facts.Velocity;
                    sb.AppendLine($"This week: {v.ThisWeek} commits, last week {v.LastWeek}, streak {v.StreakDays} days.");
                }

                if (facets.HasFlag(Facet.Repos))
                {
                    // A clean repo is not worth a line. Listing every repo means
                    // the two that need attention arrive buried in twenty that
                    // do not, which is the shape of context that gets skimmed.
                    var open = facts.Repos
                        .Where(r => r.UncommittedFiles > 0 || r.UnpushedCommits > 0)
                        .OrderByDescending(r => r.UncommittedFiles + r.UnpushedCommits)
                        .ToList();

                    if (open.Count == 0)
                    {
                        sb.AppendLine($"All {facts.Repos.Count} repos are clean and pushed.");
                    }
                    else
                    {
                        sb.AppendLine($"{open.Count} of {facts.Repos.Count} repos have something outstanding:");
                        foreach (var r in open.Take(RepoBudget))
                            sb.AppendLine($"- {r.Name} on {r.Branch}: {r.UncommittedFiles} uncommitted, {r.UnpushedCommits} unpushed");
                        if (open.Count > RepoBudget)
                            sb.AppendLine($"- (+{open.Count - RepoBudget} more, quieter)");
                    }
                }
            }
            catch (Exception ex)
            {
                // This used to be swallowed, so the model answered from a
                // thinner context than it thought it had and nothing said so.
                // Naming the gap lets it hedge instead of guessing.
                sb.AppendLine($"(Workspace facts unavailable this turn: {ex.Message})");
            }
        }

        return sb.ToString();
    }

    private string Answer(string q)
    {
        if (Contains(q, "today", "ship", "shipped", "done", "did i"))
        {
            var commits = _engine.TodayCommits();
            if (commits.Count == 0) return "No commits yet today. The day is young.";
            var lines = commits.Take(8).Select(c => $"• {c.Subject}  ({c.Repo})");
            var more = commits.Count > 8 ? $"\n…and {commits.Count - 8} more." : "";
            return $"You've made {commits.Count} commit(s) today:\n{string.Join('\n', lines)}{more}";
        }

        if (Contains(q, "left", "loose", "unfinished", "todo", "pending", "cleanup"))
        {
            var repos = _engine.DiscoverRepos();
            var bits = new List<string>();
            foreach (var path in repos)
            {
                var f = _engine.BuildLooseEnds(path);
                var name = System.IO.Path.GetFileName(path);
                if (f.Working.Total > 0) bits.Add($"• {name}: {f.Working.Total} uncommitted");
                foreach (var b in f.UnpushedBranches)
                    bits.Add($"• {name}: {b.Name} not pushed");
                if (f.TodoTotal > 0) bits.Add($"• {name}: {f.TodoTotal} TODO/FIXME");
            }
            return bits.Count == 0
                ? "Nothing dangling. Everything is committed and pushed."
                : "Here's what's still open:\n" + string.Join('\n', bits.Take(10));
        }

        if (Contains(q, "week", "velocity", "streak", "pace", "productive"))
        {
            var facts = _engine.BuildWorkspace();
            var v = facts.Velocity;
            var trend = v.ThisWeek >= v.LastWeek ? "up from" : "down from";
            return $"You've made {v.ThisWeek} commit(s) this week, {trend} {v.LastWeek} last week. " +
                   $"You're on a {v.StreakDays}-day streak" +
                   (v.BusiestRepo is not null ? $", busiest in {v.BusiestRepo}." : ".");
        }

        return "I can tell you what you shipped today, what's still open, or how your week is going. " +
               "Ask one of those, or tap a suggestion.";
    }

    private static bool Contains(string q, params string[] terms)
        => terms.Any(t => q.Contains(t, StringComparison.Ordinal));
}
