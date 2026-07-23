using System.Collections.ObjectModel;
using Avalonia.Threading;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed class MailCard : ObservableObject
{
    public MailCard(EmailDigest d)
    {
        From = d.From;
        Subject = d.Subject;
        When = d.Received is { } r ? r.ToLocalTime().ToString("ddd d MMM, HH:mm") : "";
        Dates = d.Dates;
        Links = d.Links;
        Amounts = d.Amounts;
        Actions = d.Actions;
        FullBody = d.FullBody;
        Html = d.Html;
    }

    public string From { get; }
    public string Subject { get; }
    public string When { get; }
    public IReadOnlyList<string> Dates { get; }
    public IReadOnlyList<string> Links { get; }
    public IReadOnlyList<string> Amounts { get; }
    public IReadOnlyList<string> Actions { get; }
    public string FullBody { get; }
    public string? Html { get; }

    private string _aiSummary = "";
    public string AiSummary { get => _aiSummary; set { SetProperty(ref _aiSummary, value); OnPropertyChanged(nameof(HasAiSummary)); } }
    public bool HasAiSummary => _aiSummary.Length > 0;
    public bool HasHtml => !string.IsNullOrWhiteSpace(Html);

    public bool HasDates => Dates.Count > 0;
    public bool HasLinks => Links.Count > 0;
    public bool HasAmounts => Amounts.Count > 0;
    public bool HasActions => Actions.Count > 0;

    private bool _expanded;
    public bool Expanded
    {
        get => _expanded;
        set { if (SetProperty(ref _expanded, value)) OnPropertyChanged(nameof(ToggleLabel)); }
    }

    public string ToggleLabel => _expanded ? "Hide original" : "Read original";
}

public sealed class InboxViewModel : ObservableObject, IActivatablePage
{
    private readonly PerformaEngine _engine;
    private readonly GoogleAuthService _auth = new();
    private readonly GmailService _gmail = new();
    private readonly AiService _ai = new();

    public InboxViewModel(PerformaEngine engine)
    {
        _engine = engine;
        RefreshCommand = new RelayCommand(() => _ = LoadAsync());
        ToggleCommand = new RelayCommand<MailCard>(c => { if (c is not null) c.Expanded = !c.Expanded; });
        OpenOriginalCommand = new RelayCommand<MailCard>(OpenOriginal);
        engine.GoogleSignedIn += () => _ = LoadAsync();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _timer.Tick += (_, _) => _ = LoadAsync();
        _timer.Start();

        _ = LoadAsync();
    }

    private bool _loadedOnce;
    private readonly DispatcherTimer _timer;

    /// <summary>Opening the page re-checks sign-in so mail fills itself in.</summary>
    public void OnActivated()
    {
        if (!_loadedOnce && _auth.IsSignedIn) _ = LoadAsync();
    }

    public ObservableCollection<MailCard> Mail { get; } = [];
    public RelayCommand RefreshCommand { get; }
    public RelayCommand<MailCard> ToggleCommand { get; }
    public RelayCommand<MailCard> OpenOriginalCommand { get; }

    /// <summary>
    /// Writes the message's own HTML to a temp file and opens it, so the
    /// original renders exactly as Gmail served it rather than as stripped text.
    /// </summary>
    private static void OpenOriginal(MailCard? card)
    {
        if (card?.Html is not { Length: > 0 } html) return;
        try
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"performa-mail-{Guid.NewGuid():N}.html");
            System.IO.File.WriteAllText(path, html);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.IO.IOException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private bool _loading;
    public bool Loading { get => _loading; set => SetProperty(ref _loading, value); }

    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private bool _connected;
    public bool Connected { get => _connected; set => SetProperty(ref _connected, value); }

    /// <summary>
    /// Adds prose on top of the extraction. The facts already on the card are
    /// never removed, so a failed or skipped call costs nothing.
    /// </summary>
    private async Task SummariseAsync()
    {
        foreach (var card in Mail.Take(6))
        {
            var prose = await _ai.SummariseEmailAsync(
                _engine.Prefs, card.From, card.Subject, card.FullBody);
            if (prose is { Length: > 0 }) card.AiSummary = prose;
        }
    }

    public async Task LoadAsync()
    {
        Connected = _auth.IsSignedIn;
        if (!Connected)
        {
            Status = "Connect your Google account in Settings to see your inbox.";
            return;
        }

        var creds = GoogleCredentialStore.Load(_engine.Prefs);
        if (creds is null) { Status = "No Google client configured."; return; }

        Loading = true;
        Status = "Reading your inbox…";

        var token = await _auth.GetAccessTokenAsync(creds.ClientId, creds.ClientSecret);
        if (token is null)
        {
            Status = "Google sign-in expired. Reconnect in Settings.";
            Connected = false;
            Loading = false;
            return;
        }

        var mail = await _gmail.GetRecentAsync(token);
        _loadedOnce = true;
        Mail.Clear();
        foreach (var m in mail) Mail.Add(new MailCard(m));

        _ = SummariseAsync();

        Status = mail.Count == 0
            ? "Nothing new in the last three days."
            : $"{mail.Count} message(s) from the last three days. Nothing is summarised: every date, link and request is listed, and the original is one click away.";
        Loading = false;
    }
}
