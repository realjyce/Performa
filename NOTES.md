# NOTES

One decision or lesson per entry. Newest last.

- **.NET 10 LTS confirmed as target** (2026-07-20). Checked, not assumed:
  .NET 10 is current LTS until Nov 2028; .NET 8 support ends Nov 2026. The
  machine only had the 8 SDK, so 10 was installed via winget.

- **Shell out to git rather than LibGit2Sharp.** LibGit2Sharp's native
  binaries fight Native AOT and single-file publishing; `git` is guaranteed
  present on any machine this tool matters on. Parsers read porcelain output
  with unit separators (\x1f/\x1e/\x1d), so they're pure string functions and
  unit-testable without a repo.

- **System.CommandLine 2.0** for the CLI: Microsoft-maintained, AOT-safe,
  gives --help/completions for free. Considered Spectre.Console.Cli but its
  reflection model is AOT-hostile.

- **Daemon cut from v1** (user approved). "Since last standup" is a watermark
  in state.json; AOT startup is fast enough that residency buys nothing for
  these four outputs.

- **The AI seam is exactly one interface.** IEnricher takes structured facts
  records and returns rendered text. FactBuilders never format; the enricher
  never runs git. That boundary is what an AI implementation replaces later.

- **Adaptation is counters, not learning.** Edit-length ratio moves verbosity;
  two consecutive rejects cycle grouping; accept resets streaks. All in
  Adaptation.Apply, fully unit-tested.

- **JSON via source generation** (PerformaJsonContext) because reflection-based
  System.Text.Json doesn't survive Native AOT.

- **Integration tests build a throwaway real repo** in temp with pinned
  GIT_AUTHOR_DATE values, then run the actual pipeline. This is the "verify
  against real git history" bar the brief set.

- **Native AOT needs the MSVC linker**, which isn't installed here (VS C++
  Build Tools workload, multi-GB). Shipped single-file self-contained trimmed
  instead: 14 MB, ~180 ms startup, zero trim warnings. To flip to true AOT:
  install "Desktop development with C++" Build Tools, then
  `dotnet publish -c Release -r win-x64` (csproj already sets PublishAot).

- **DefaultBranch must return the LOCAL name.** First shakedown against the
  cloned R.A.T.S repo flagged `main` itself as stale because
  origin/HEAD resolves to "origin/main" and branch-name comparisons used the
  remote-qualified form. Real repos catch what scratch repos don't.

- **Classifier: "Update README.md" is Docs, not Other.** Real commit subjects
  are verb-first ("Update", "Edit"), so the docs rule also matches any
  subject containing "readme".

- **Two-face output, decided at the TTY boundary.** Pretty ANSI (hand-rolled,
  ~40 lines, no Spectre dependency) when stdout is a terminal; clean markdown
  when piped. Editing always operates on the markdown render so notepad never
  sees escape codes. Console.OutputEncoding must be forced to UTF-8 on
  Windows or the glyphs mojibake.

- **Bare `performa` is the dashboard**: repo stamp, today's commits since the
  standup watermark, loose ends, command hints. This is the "combined"
  productivity-manager face; the subcommands stay single-purpose.

- **Workspace dashboard scans one folder, depth one.** A repo is any direct
  child with a .git directory. No recursive crawl, no registry of paths to
  maintain. Velocity (week-over-week commits, day streak, busiest repo) comes
  from a dates-only git log per repo, so the whole scan stays fast.

- **Parallel tests must not share GIT_AUTHOR_DATE.** Two suites that set the
  process-global env vars raced and backdated each other's commits. Both now
  sit in one xUnit collection so they serialize. Env vars are global state;
  test suites forget that.
