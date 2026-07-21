using System.Collections.ObjectModel;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

public sealed class EventCard(CalendarEvent e)
{
    public string Title { get; } = e.Title;
    public string Colour { get; } = e.ColourHex;
    public string Calendar { get; } = e.CalendarName;
    public string? Location { get; } = e.Location;
    public bool HasLocation { get; } = !string.IsNullOrWhiteSpace(e.Location);

    public string Day { get; } = e.Start is { } s
        ? (s.Date == DateTimeOffset.Now.Date ? "Today"
            : s.Date == DateTimeOffset.Now.Date.AddDays(1) ? "Tomorrow"
            : s.ToString("ddd d MMM"))
        : "";

    public string Time { get; } = e.AllDay
        ? "All day"
        : e.Start is { } st
            ? (e.End is { } en ? $"{st:HH:mm} – {en:HH:mm}" : st.ToString("HH:mm"))
            : "";
}

public sealed class CalendarViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;
    private readonly GoogleAuthService _auth = new();
    private readonly GoogleCalendarService _calendar = new();

    public CalendarViewModel(PerformaEngine engine)
    {
        _engine = engine;
        RefreshCommand = new RelayCommand(() => _ = LoadAsync());
        _ = LoadAsync();
    }

    public ObservableCollection<EventCard> Events { get; } = [];
    public RelayCommand RefreshCommand { get; }

    private bool _loading;
    public bool Loading { get => _loading; set => SetProperty(ref _loading, value); }

    private string _status = "";
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    private bool _connected;
    public bool Connected { get => _connected; set => SetProperty(ref _connected, value); }

    public async Task LoadAsync()
    {
        Connected = _auth.IsSignedIn;
        if (!Connected)
        {
            Status = "Connect your Google account in Settings to see your week.";
            return;
        }

        var creds = GoogleCredentialStore.Load(_engine.Prefs);
        if (creds is null) { Status = "No Google client configured."; return; }

        Loading = true;
        Status = "Reading your calendar…";

        var token = await _auth.GetAccessTokenAsync(creds.ClientId, creds.ClientSecret);
        if (token is null)
        {
            Status = "Google sign-in expired. Reconnect in Settings.";
            Connected = false;
            Loading = false;
            return;
        }

        var events = await _calendar.GetUpcomingAsync(token);
        Events.Clear();
        foreach (var e in events) Events.Add(new EventCard(e));

        Status = events.Count == 0
            ? "Nothing scheduled in the next seven days."
            : $"{events.Count} event(s) in the next seven days.";
        Loading = false;
    }
}
