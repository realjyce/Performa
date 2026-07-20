using Recap.Prefs;
using Xunit;

namespace Recap.Tests;

public class AdaptationTests
{
    [Fact]
    public void Heavy_edit_shrink_lowers_verbosity()
    {
        var prefs = new Preferences { Verbosity = Verbosity.Normal };
        var note = Adaptation.Apply(prefs, FeedbackAction.Edit, originalLength: 1000, editedLength: 400);
        Assert.Equal(Verbosity.Terse, prefs.Verbosity);
        Assert.NotNull(note);
    }

    [Fact]
    public void Heavy_edit_growth_raises_verbosity()
    {
        var prefs = new Preferences { Verbosity = Verbosity.Normal };
        Adaptation.Apply(prefs, FeedbackAction.Edit, originalLength: 1000, editedLength: 1600);
        Assert.Equal(Verbosity.Detailed, prefs.Verbosity);
    }

    [Fact]
    public void Small_edit_changes_nothing()
    {
        var prefs = new Preferences { Verbosity = Verbosity.Normal };
        var note = Adaptation.Apply(prefs, FeedbackAction.Edit, originalLength: 1000, editedLength: 950);
        Assert.Equal(Verbosity.Normal, prefs.Verbosity);
        Assert.Null(note);
    }

    [Fact]
    public void Verbosity_never_drops_below_terse()
    {
        var prefs = new Preferences { Verbosity = Verbosity.Terse };
        Adaptation.Apply(prefs, FeedbackAction.Edit, 1000, 100);
        Assert.Equal(Verbosity.Terse, prefs.Verbosity);
    }

    [Fact]
    public void Two_rejects_cycle_grouping_and_reset_streak()
    {
        var prefs = new Preferences { Grouping = Grouping.Area };
        Assert.Null(Adaptation.Apply(prefs, FeedbackAction.Reject));
        Assert.Equal(Grouping.Area, prefs.Grouping);
        var note = Adaptation.Apply(prefs, FeedbackAction.Reject);
        Assert.Equal(Grouping.Kind, prefs.Grouping);
        Assert.Equal(0, prefs.RejectStreak);
        Assert.NotNull(note);
    }

    [Fact]
    public void Accept_resets_reject_streak()
    {
        var prefs = new Preferences();
        Adaptation.Apply(prefs, FeedbackAction.Reject);
        Adaptation.Apply(prefs, FeedbackAction.Accept);
        Adaptation.Apply(prefs, FeedbackAction.Reject);
        Assert.Equal(Grouping.Area, prefs.Grouping);
    }
}
