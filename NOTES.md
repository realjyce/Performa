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

- **Google sign-in uses loopback + PKCE**, the flow Google recommends for
  installed apps. A TcpListener on a random loopback port catches the redirect
  (deliberately not HttpListener, which wants a URL ACL / admin on Windows).
  Scopes are calendar.readonly and gmail.readonly, nothing writable. Tokens go
  to %APPDATA%/performa/google.json; the client secret stays in prefs. Refresh
  happens automatically two minutes before expiry.

- **Depth pass.** BoxShadow on cards (heavier on hover). Note BoxShadow lives
  on Border, not Button, so the quick-action buttons keep border+hover instead.

- **GitHub list alignment.** The action column is a fixed 86px with
  right-aligned content, so "on disk" and Clone line up regardless of repo
  name length; names ellipsise rather than pushing the column.

- **Credentials ship with the app, not with the user.** GoogleCredentialStore
  resolves: prefs override (dev) -> file next to the binary (product) ->
  AppData (this machine). The file is gitignored and verified with
  git check-ignore, because the repo is public. For installed apps Google
  treats the client secret as non-secret; PKCE is the real protection.

- **Commercial blocker worth remembering:** gmail.readonly is a RESTRICTED
  scope. Shipping it to real users needs Google verification plus a CASA
  security assessment (thousands of dollars, months). calendar.readonly is
  only "sensitive", far lighter. Testing mode is free but expires refresh
  tokens weekly.

- **Email digest is extraction, not summarisation.** Asks, dates, amounts and
  links are pulled out with regex and listed verbatim; the untouched body sits
  behind "Read original". That is the only honest way to claim no information
  is lost, since summarising is lossy by definition.

- **Calendar folded into Daily.** One page answers "what does today look like":
  tasks and notes on the left, schedule and today's commits on the right. The
  standalone Calendar page and its view model are gone; GoogleCalendarService
  stayed. Nav order is Dashboard, Daily, Inbox, then the git pages.

- **Pages refresh themselves once Google is connected.** Two triggers: an
  engine-level GoogleSignedIn event that Settings raises on success, and an
  IActivatablePage.OnActivated call fired when the sidebar selection changes.
  So signing in mid-session fills Daily and Inbox without a restart, and
  opening either page re-checks the session.

- **Auto-refresh intervals, chosen deliberately:** dashboard git rescan every
  3 minutes (local, but each scan spawns processes), Google calendar and Gmail
  every 5 minutes (respects API quota and battery). Refresh controls are
  circular arrows that spin only while a fetch is actually running.

- **Username is asked, never inferred.** A first-run overlay collects it and it
  is editable in Settings, so nothing is silently taken from a Google profile.

- **Email fidelity.** The digest keeps the message's own text/html part, and
  "Open as Gmail sent it" writes it to a temp file and opens the browser, so
  the original renders exactly as sent rather than as stripped text. Avalonia
  has no HTML renderer, so the browser is the honest route.

- **AI is additive and opt-in.** GeminiService sits behind the same seam as the
  deterministic enricher. The Assistant computes real git facts first and only
  then asks the model, passing those facts as context with an instruction never
  to invent. Email keeps its full structured extraction and gains an "AI READ"
  block on top. Every failure path returns null, so the deterministic answer is
  always what ships. Nothing leaves the machine unless AiEnabled is true.

- **Assistant is now the one premium surface**: violet gradient card above the
  Settings container, taller, with an "AI arriving soon" pill. The greeting
  ("Hello, name") shares the quiet container with Settings at small size.

- **GitHub sign-in uses the device flow, not the web flow.** The web flow needs
  a client secret, and a secret shipped inside a distributed desktop build is
  not a secret. The device flow needs only a client id: GitHub returns a short
  code, the user approves it on github.com, and Performa polls for the token.
  Nothing worth stealing is embedded in the binary. The token lands in
  %APPDATA%/performa/github.json, never in the repo.

- **One place decides which GitHub credential to use.** PerformaEngine
  .GitHubAccessToken prefers a device-flow sign-in and falls back to a pasted
  personal token, so the dashboard and the settings scan can never disagree
  about who is signed in.

- **Preferences are round-trip tested.** They serialise through a source
  generator, and a property the generator does not see is dropped silently on
  save. That reads to the user as "my token never saved", so every stored field
  is asserted rather than trusted.

- **Obsolete Avalonia APIs cleared.** Watermark to PlaceholderText and
  SystemDecorations to WindowDecorations. The build is warning-free again, so a
  real warning stays visible instead of hiding in the noise.
