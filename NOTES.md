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

- **Desktop = Avalonia over the same Core.** Split into Performa.Core (engine),
  Performa.Cli, and Performa.Desktop so the GUI and CLI share one code path.
  Hand-rolled MVVM (ObservableObject + RelayCommand + a ViewLocator), no MVVM
  framework dependency. Custom dark chrome via SystemDecorations=None; nav is
  two ListBoxes both bound to one Selected property, which cross-clears cleanly.

- **Verify the UI like the web work.** tools/Shot is an Avalonia.Headless + Skia
  harness that renders any page to PNG (and can drive the assistant) so the GUI
  is checked by screenshot, not by assertion. Same discipline as the puppeteer
  shots.

- **Force InvariantCulture in App.Initialize.** The machine locale rendered the
  Daily date in Japanese; pinning culture fixes date formatting app-wide without
  touching each call site.

- **Avalonia 12 clipboard API churned** (DataObject/DataFormats obsolete, text
  methods moved). Not worth chasing for v1 — report text sits in a
  SelectableTextBlock, so native Ctrl+C works. Revisit if a Copy button earns
  its place.

- **GitHub remote data lives in Desktop only.** GitHubService (HttpClient to
  api.github.com) is in Performa.Desktop, never Core, so the CLI keeps its
  no-network guarantee. Works unauthenticated for public repos (60/hr); an
  optional token in Settings raises the limit and reaches private repos. Repos
  with no GitHub origin simply show no remote line. Claude API stays a dormant
  seam per the brief; only a stored token field exists, no model calls.

- **Dashboard quick actions navigate, they don't duplicate.** The four cards
  (standup/changelog/recap/loose) call back into MainViewModel, which selects
  the right page and presets it, rather than re-implementing report logic on
  the dashboard.

- **Workspace is pickable and live now.** Settings has a native folder picker
  (Avalonia StorageProvider, in view code-behind where TopLevel is available).
  Changing it calls engine.SetWorkspace, which raises WorkspaceChanged; the
  dashboard, reports, and loose-ends pages subscribe and reload. No restart.

- **Embedded CLI console.** A terminal drawer (toggle in the title bar, one
  click) runs the same engine as the `performa` binary: standup, changelog,
  summary <repo>, loose, repos, help, clear. Claude-Code-style, so the CLI is
  always one keystroke away without leaving the app. ConsoleViewModel parses
  and dispatches; output is mono text.

- **Two-list nav clobbered itself.** Both sidebar lists (main + utility) bound
  SelectedItem to one Selected property; selecting a utility item made the main
  list clear and write null back, blanking the page. Fix: ignore null writes in
  the Selected setter. This is why Settings navigated to an empty page. Also
  moved the settings folder-picker button off runtime FindControl onto a XAML
  Click handler so ViewLocator rebuilds can't NRE it.

- **Console auto-focuses now.** When the drawer opens, the input takes focus
  (PropertyChanged -> Dispatcher.Post -> Focus), and clicking anywhere in the
  drawer focuses it too. No more hunting for the transparent textbox.

- **Carbon theme.** Surfaces lifted off near-black to a cool carbon (#1C1E22
  base) with two soft gradients: a diagonal backdrop for the shell and a
  lighter one behind content so panels read forward. Echoes the portfolio
  hero without its contrast.

- **Repos come from two sources now.** Local: auto-detect on launch when the
  workspace is unset or empty (scans the usual dev folders and picks the one
  with the most repos), plus Change/Auto-detect/Rescan buttons. Remote: a
  GitHub token lists every repo you can see including private, with one-click
  clone into the workspace. Clone uses git's own credential helper so no token
  is ever written into .git/config.

- **Console windows were flashing on every git call.** GitRunner's
  ProcessStartInfo never set CreateNoWindow/UseShellExecute, so each git
  invocation spawned a console — dozens per workspace scan in a GUI. Fixed at
  the source in Core, so the CLI benefits too.

- **Carbon is a weave, not just a tone.** An 8px PNG tile (offset light/dark
  dots at very low alpha) tiled 1:1 as an ImageBrush over the sidebar, title
  bar, and content backdrop. Cards keep solid fills so they read forward.
