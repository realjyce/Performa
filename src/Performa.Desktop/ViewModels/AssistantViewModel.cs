using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed class ChatMessage(bool isUser, string text)
{
    public bool IsUser { get; } = isUser;
    public bool IsAssistant => !IsUser;
    public string Text { get; } = text;
}

/// <summary>
/// Deterministic assistant. Answers from git facts today; the same ask/answer
/// shape sits behind the enrichment seam an AI model replaces later.
/// </summary>
public sealed class AssistantViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;

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
        var answer = await Task.Run(() => Answer(question.ToLowerInvariant()));
        Messages.Add(new ChatMessage(false, answer));
        Thinking = false;
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
