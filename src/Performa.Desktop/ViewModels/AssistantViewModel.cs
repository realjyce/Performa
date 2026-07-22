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
    private readonly GeminiService _gemini = new();

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

        var key = AppCredentialStore.GeminiKey(_engine.Prefs);
        if (_engine.Prefs.AiEnabled && !string.IsNullOrWhiteSpace(key))
        {
            var context = await Task.Run(BuildContext);
            var answer = await _gemini.AskAsync(key, context, question);
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

    /// <summary>Only real git facts go to the model; it is never asked to invent.</summary>
    private string BuildContext()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are Performa, a developer's assistant. Facts about their work:");

        var today = _engine.TodayCommits();
        sb.AppendLine($"Commits today: {today.Count}");
        foreach (var (repo, when, subject) in today.Take(15))
            sb.AppendLine($"- {when:HH:mm} [{repo}] {subject}");

        try
        {
            var facts = _engine.BuildWorkspace();
            var v = facts.Velocity;
            sb.AppendLine($"This week: {v.ThisWeek} commits, last week {v.LastWeek}, streak {v.StreakDays} days.");
            foreach (var r in facts.Repos)
                sb.AppendLine($"- {r.Name} on {r.Branch}: {r.UncommittedFiles} uncommitted, {r.UnpushedCommits} unpushed");
        }
        catch (Exception) { }

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
