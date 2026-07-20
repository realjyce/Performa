namespace Recap.Git;

public static class GitLogParser
{
    public const string RecordSeparator = "\x1e";
    public const string FieldSeparator = "\x1f";
    public const string BodyTerminator = "\x1d";

    public const string LogFormat =
        "%x1e%H%x1f%an%x1f%ae%x1f%aI%x1f%s%x1f%b%x1d";

    public static List<Commit> Parse(string raw)
    {
        var commits = new List<Commit>();
        foreach (var record in raw.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var terminator = record.IndexOf(BodyTerminator, StringComparison.Ordinal);
            if (terminator < 0) continue;

            var fields = record[..terminator].Split(FieldSeparator);
            if (fields.Length < 6) continue;

            var files = record[(terminator + 1)..]
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            commits.Add(new Commit(
                Sha: fields[0].Trim(),
                Author: fields[1],
                Email: fields[2],
                Date: DateTimeOffset.Parse(fields[3]),
                Subject: fields[4].Trim(),
                Body: fields[5].Trim(),
                Files: files));
        }
        return commits;
    }
}
