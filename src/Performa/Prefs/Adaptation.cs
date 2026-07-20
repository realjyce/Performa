namespace Performa.Prefs;

public enum FeedbackAction { Accept, Edit, Reject }

public static class Adaptation
{
    /// <summary>
    /// Deterministic preference refinement. Returns a human-readable note
    /// when a preference changed, null otherwise.
    /// </summary>
    public static string? Apply(
        Preferences prefs, FeedbackAction action,
        int originalLength = 0, int editedLength = 0)
    {
        switch (action)
        {
            case FeedbackAction.Accept:
                prefs.RejectStreak = 0;
                return null;

            case FeedbackAction.Edit:
                prefs.RejectStreak = 0;
                if (originalLength == 0) return null;
                var ratio = (double)editedLength / originalLength;
                if (ratio < 0.6 && prefs.Verbosity > Verbosity.Terse)
                {
                    prefs.Verbosity--;
                    return $"You trimmed that a lot. Dialing verbosity down to {prefs.Verbosity}.";
                }
                if (ratio > 1.4 && prefs.Verbosity < Verbosity.Detailed)
                {
                    prefs.Verbosity++;
                    return $"You added detail. Raising verbosity to {prefs.Verbosity}.";
                }
                return null;

            case FeedbackAction.Reject:
                prefs.RejectStreak++;
                if (prefs.RejectStreak >= 2)
                {
                    prefs.RejectStreak = 0;
                    prefs.Grouping = prefs.Grouping switch
                    {
                        Grouping.Area => Grouping.Kind,
                        Grouping.Kind => Grouping.Flat,
                        _ => Grouping.Area,
                    };
                    return $"Two rejections in a row. Switching grouping to {prefs.Grouping}.";
                }
                return null;

            default:
                return null;
        }
    }
}
