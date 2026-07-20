using Performa.Prefs;
using Performa.Reports;

namespace Performa.Enrich;

/// <summary>
/// The single seam between structured git facts and rendered prose.
/// v1 ships one deterministic implementation. An AI-backed implementation
/// can replace it later without touching fact-building or the CLI.
/// </summary>
public interface IEnricher
{
    string RenderStandup(StandupFacts facts, Preferences prefs);
    string RenderChangelog(ChangelogFacts facts, Preferences prefs);
    string RenderSummary(SummaryFacts facts, Preferences prefs);
    string RenderLooseEnds(LooseEndsFacts facts, Preferences prefs);
}
