using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Performa.Desktop.Services;

public sealed record CalendarEvent(
    string Title,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    bool AllDay,
    string? Location,
    string ColourHex,
    string CalendarName);

/// <summary>
/// Read-only Google Calendar. Event colours come from Google's own palette so
/// the cards match the colours already set on the user's calendars.
/// </summary>
public sealed class GoogleCalendarService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Fallback palette if the colours endpoint is unavailable.
    private const string DefaultColour = "#7C6CF0";

    private Dictionary<string, string>? _eventColours;
    private Dictionary<string, string>? _calendarColours;

    public async Task<List<CalendarEvent>> GetUpcomingAsync(string accessToken, int days = 7)
    {
        await LoadPaletteAsync(accessToken);

        var calendars = await GetCalendarsAsync(accessToken);
        var events = new List<CalendarEvent>();
        var from = DateTimeOffset.Now.Date;
        var to = from.AddDays(days);

        foreach (var (id, name, calColourId) in calendars)
        {
            var url =
                $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(id)}/events" +
                $"?timeMin={Uri.EscapeDataString(new DateTimeOffset(from).ToString("o"))}" +
                $"&timeMax={Uri.EscapeDataString(new DateTimeOffset(to).ToString("o"))}" +
                "&singleEvents=true&orderBy=startTime&maxResults=50";

            var doc = await GetJsonAsync(url, accessToken);
            if (doc is null) continue;
            using var _ = doc;

            if (!doc.RootElement.TryGetProperty("items", out var items)) continue;

            foreach (var item in items.EnumerateArray())
            {
                var title = item.TryGetProperty("summary", out var s)
                    ? s.GetString() ?? "(no title)" : "(no title)";
                var (start, allDay) = ReadTime(item, "start");
                var (end, _) = ReadTime(item, "end");
                var location = item.TryGetProperty("location", out var l) ? l.GetString() : null;

                var colour = DefaultColour;
                if (item.TryGetProperty("colorId", out var ec)
                    && ec.GetString() is { } ecid
                    && _eventColours?.TryGetValue(ecid, out var hex) == true)
                    colour = hex;
                else if (calColourId is { } cc
                    && _calendarColours?.TryGetValue(cc, out var chex) == true)
                    colour = chex;

                events.Add(new CalendarEvent(title, start, end, allDay, location, colour, name));
            }
        }

        return [.. events.OrderBy(e => e.Start ?? DateTimeOffset.MaxValue)];
    }

    private static (DateTimeOffset?, bool) ReadTime(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var node)) return (null, false);
        if (node.TryGetProperty("dateTime", out var dt)
            && DateTimeOffset.TryParse(dt.GetString(), out var parsed))
            return (parsed, false);
        if (node.TryGetProperty("date", out var d)
            && DateTimeOffset.TryParse(d.GetString(), out var day))
            return (day, true);
        return (null, false);
    }

    private async Task<List<(string Id, string Name, string? ColourId)>> GetCalendarsAsync(string token)
    {
        var doc = await GetJsonAsync(
            "https://www.googleapis.com/calendar/v3/users/me/calendarList", token);
        if (doc is null) return [];
        using var _ = doc;

        var list = new List<(string, string, string?)>();
        if (!doc.RootElement.TryGetProperty("items", out var items)) return list;

        foreach (var item in items.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (id is null) continue;
            var name = item.TryGetProperty("summary", out var s) ? s.GetString() ?? id : id;
            var colourId = item.TryGetProperty("colorId", out var c) ? c.GetString() : null;
            list.Add((id, name, colourId));
        }
        return list;
    }

    private async Task LoadPaletteAsync(string token)
    {
        if (_eventColours is not null) return;
        _eventColours = [];
        _calendarColours = [];

        var doc = await GetJsonAsync("https://www.googleapis.com/calendar/v3/colors", token);
        if (doc is null) return;
        using var _ = doc;

        if (doc.RootElement.TryGetProperty("event", out var ev))
            foreach (var p in ev.EnumerateObject())
                if (p.Value.TryGetProperty("background", out var bg) && bg.GetString() is { } hex)
                    _eventColours[p.Name] = hex;

        if (doc.RootElement.TryGetProperty("calendar", out var cal))
            foreach (var p in cal.EnumerateObject())
                if (p.Value.TryGetProperty("background", out var bg) && bg.GetString() is { } hex)
                    _calendarColours[p.Name] = hex;
    }

    private static async Task<JsonDocument?> GetJsonAsync(string url, string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            await using var stream = await res.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (JsonException) { return null; }
    }
}
