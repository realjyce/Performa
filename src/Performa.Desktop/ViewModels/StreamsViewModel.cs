using Performa.Desktop.Infrastructure;

namespace Performa.Desktop.ViewModels;

public sealed class StreamTile(string name, string blurb, string status)
{
    public string Name { get; } = name;
    public string Blurb { get; } = blurb;
    public string Status { get; } = status;
}

public sealed class StreamsViewModel : ObservableObject
{
    // The dormant seams. Live today: the git stream (already powering the app).
    // The rest are honest "later" tiles behind the same enrichment interface.
    public StreamTile[] Live { get; } =
    [
        new("Codebase", "Reads your local git history. Powers every report and the dashboard.", "Active"),
    ];

    public StreamTile[] Dormant { get; } =
    [
        new("AI summaries", "Prose-quality write-ups behind the same IEnricher seam, when you add an inference budget.", "Later"),
        new("Email", "Turn threads into daily digests alongside your commits.", "Later"),
        new("Calendar", "Fold meetings and deadlines into the daily view.", "Later"),
        new("Discord", "Surface what your team shipped, in your voice.", "Later"),
    ];
}
