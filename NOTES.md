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

- **A valid Gemini key can still return 429 limit:0.** The 2.x flash models are
  legacy and carry no free-tier allocation, so the error looks like a billing or
  project problem when it is really a model choice. Listing /v1beta/models with
  the same key proved the key was fine. Now on gemini-flash-lite-latest with
  gemini-3.1-flash-lite behind it, because the "latest" alias returns 503 under
  load often enough to be worth a fallback. Lesson: read which metric the quota
  error names before blaming the credential.

- **Fluent resource keys beat Style setters.** Setting Background on a TextBox
  or Button does nothing on hover, focus or press, because the control template
  paints from its own resource lookup. That is why the fields kept flashing a
  dark well and the accent buttons stayed system blue while every token said
  violet. The fix belongs at the key (TextControlBackgroundFocused,
  ComboBoxDropDownBackground) or on /template/ ContentPresenter, not the selector.

- **Two themes, one token layer.** Tokens.axaml is now ThemeDictionaries and
  every view already reached its brushes through DynamicResource, so the switch
  re-themes a live window with no reload and no per-view work. Paper darkens the
  accent because the dark-theme violet fails contrast on white, and gets its own
  weave tile with the light/dark roles swapped.

- **Inputs are a bare caret.** No box, no border, nothing that lights up. The
  card is the container; a second container inside it was the noise.

- **Dropdown popups are pinned to the button width** so a long option cannot
  make the list wider than the control it drops out of. Options trim instead of
  wrapping, which keeps every row one line tall.

- **The thinking mark is drawn, not composed.** Three dots wave, gather into a
  rotating prism, collapse into one breathing circle, then split back. Keyframes
  on separate elements drift at the seams; one clock driving one Render cannot.
  Growth is held back until the dots have nearly met, and shrink runs ahead of
  separation, otherwise the halfway frames read as a lumpy blob. Verified as a
  filmstrip of real frames rather than assumed.

- **Zero-setup credentials.** AppCredentialStore resolves the GitHub client id
  and the Gemini key the same way Google's already worked: prefs override, then
  a file beside the binary, then AppData. The GitHub client id is public by
  design in the device flow. The Gemini key is not, and shipping it is a
  testing-phase compromise, recorded as such in the code.

- **Shipping build is a single self-contained exe.** One Performa.exe, no .NET
  needed on the target machine. Trimming stays off on purpose: Avalonia resolves
  controls and converters by reflection, so a trimmed build publishes clean and
  then fails at runtime, which is the worst place to find out. 47 MB is the
  price of that safety and it is the right trade.

- **Renaming the assembly breaks every avares URI.** They are keyed on assembly
  name, so AssemblyName=Performa turned five working resource lookups into
  runtime failures the compiler said nothing about. Renamed together, and a
  publish is not trusted until the exe is launched and shown to hold a window.

- **AI ships on.** A build that needs a checkbox ticked before it answers is not
  zero-setup. The Settings switch still turns it off, the deterministic answers
  are identical either way, and the model that answered is named on every reply
  so its prose is never mistaken for the facts.

- **Credential resolution is tested, not assumed.** It decides whether a shipped
  build works for someone who types nothing, so there is a test asserting an
  empty Preferences still resolves the file beside the binary, and that a key
  entered in Settings beats it.

- **Installer is per-user, not Program Files.** Installing under LOCALAPPDATA
  needs no administrator rights, and an elevation prompt on an unsigned binary
  is exactly what makes people cancel. Gets a Start Menu entry, an optional
  desktop shortcut, and a real Add/Remove Programs entry with an uninstaller.
  Verified by wiping the install, running setup silently, and launching from
  the shortcut it created.

- **Credentials are copied with skipifsourcedoesntexist.** A build made on a
  machine without them still compiles and runs; it just asks for a key in
  Settings instead of failing at install time.

- **One build recipe, one artefact.** The Inno script is generated inside
  publish.ps1 rather than kept as a checked-in .iss, and the intermediate
  publish folder is deleted once the installer has swallowed it. Two recipes
  drift apart; one cannot. PerformaSetup.exe lands in the repo root because a
  deliverable buried two folders deep gets confused for a build leftover.

- **GitHub sign-in runs at launch.** The first-run overlay became a two-step
  walkthrough: name, then Connect accounts. It shows once (OnboardingDone in
  prefs) and skips any step already satisfied, so a returning user with both
  accounts connected never sees it. Device flow shows the user code in the
  overlay itself and polls until GitHub approves.

- **Device flow needs no client secret**, unlike Google. The GitHub client id is
  public by design, so a shipped binary carries nothing worth stealing. The
  OAuth App must have "Device flow" enabled or GitHub rejects the grant type.

- **Launch at boot uses the per-user Run key** (HKCU), not a machine-wide entry
  or a scheduled task: no admin rights needed, it appears in Task Manager's
  Startup tab where the user can disable it themselves, and removal is one
  delete. The toggle reads the live registry rather than a stored flag, so it
  stays honest if the user turns it off outside the app. Verified by writing
  and removing the entry against the real registry.

- **Boot launch passes --startup**, which brings the window up minimised so it
  is ready without stealing focus during login. The registry value stores the
  path from Environment.ProcessPath, so enable it from the published exe rather
  than a debug run if you want it pointing at the real binary.

- **The refresh mark is two opposing arrows.** Chosen for its 180-degree
  rotational symmetry: a full spin shows the same silhouette twice, so the
  rotation reads as one continuous smooth motion instead of a lopsided arrow
  chasing its own tail.

- **Every auto-refreshing surface stamps "Updated HH:mm".** A timer that
  refreshes silently is indistinguishable from one that stopped working; the
  stamp is the difference between trusting the number and wondering about it.

- **Activity fills the history gap.** Dashboard is now, Daily is today,
  Reports are write-ups; nothing answered "what was I doing on Tuesday".
  Activity is two weeks of your own commits grouped by day, from the same
  CommitsSince plumbing TodayCommits already used.

- **NeonBrush exists for exactly one word: the user's name.** An accent used
  everywhere stops being an accent. Brighter than AccentBright on carbon,
  deepened on paper where electric violet washes out.

- **The spin wobble was geometry, not animation.** The refresh glyph's bounds
  are taller than wide, so rotating the Path swept an off-centre envelope. The
  fix renders the glyph in its native square canvas inside a Viewbox and
  rotates that: the pivot is the arc circle's true centre for any icon.

- **Smooth scrolling is one attached behavior, applied from a style.** Wheel
  input eases the offset toward a target with exponential decay against real
  elapsed time - never a per-frame constant, which runs at different speeds on
  different refresh rates (the portfolio deck bug, not repeated).

- **Pulls replaced Streams.** Streams' tiles still called Email, Calendar and
  AI "later" after all three had shipped - a menu item that lies is worse than
  an empty one. Pulls is GitHub's own "involves:@me" search: Inbox is what
  email asks of you, Pulls is what code asks of you.

- **Loose Ends always shows the health roster.** A clean workspace used to be
  one tick in an empty page; now every repo states its branch and verdict, so
  "clean" is a claim per repository instead of an absence of content.

- **The sidebar collapses to an icon rail.** One width property with a
  transition; labels hide via a .rail class on their containers, so no view
  duplicates its layout. The workspace header became a picker button - the
  folder you are looking at should be switchable where it is displayed.

- **The Daily Brief is the lifestyle seam.** One paragraph over the whole day:
  meetings, open tasks, commits, streak. The deterministic sentence is computed
  first and always shown; a model only ever rewrites those same facts into
  something warmer, and names itself under the paragraph. The greeting follows
  the clock, not a canned string.

- **Every loose end carries its next step.** A finding without an action is
  just nagging; "21 uncommitted files" now ends with what to do about it. The
  actionable list leads the page, the per-repo roster closes it: action first,
  state second.

- **One clock, every rule.** AutomationService replaced five per-page timers.
  Rules that span streams only become possible once one loop sees them all:
  "meeting in 10 minutes AND code sitting uncommitted" is a sentence no
  single-page timer could have written.

- **Automations announce themselves or they are spyware.** Every rule has its
  own switch in Settings, every artifact it writes carries a stamp saying it
  was automatic and when, and nothing outbound ever fires - it reads, prepares
  and notifies, never sends or pushes. Silent magic is how trust dies.

- **A late brief is worse than no brief.** The first build fired "Good morning"
  at 22:11, because the catch-up rule only checked "past the hour". It is wrong
  about the day it claims to describe. The window is now four hours and the
  decision is a pure static method with tests, not a condition buried in a timer.

- **Daily's Save had to become read-modify-write.** The automation loop writes
  the close-out and suggestions into the same file; building a fresh DailyData
  on every task tick would have erased its work silently.

- **The task ring was cut on the evidence.** A two-slice donut is a documented
  anti-pattern; a ratio against a limit is a meter. It is a thin track with the
  count beside it, which is smaller, faster to read and honest.

- **Charts follow the mark spec, not taste.** Single hue with today emphasised
  rather than a value-ramp: bar height already encodes the count, so colouring
  by size would say the same thing twice. Capped 24px bars, 4px data-end square
  at the baseline, no gridlines, axis band inside the card so it cannot clip.

- **Never mark an automation fired before it has fired.** CloseoutAsync marked
  the rule done and then did the work; a failure anywhere after that burned the
  whole day - no retry, and yesterday's close-out sat on the page looking like
  today's. It reported "Shipped 0 commit(s)" while two commits were listed a
  card away. Marking now happens after the artifact lands.

- **A single unreadable repo could zero the day.** CommitsBack had no per-repo
  guard, so one GitException emptied the count - and on the automation clock,
  where exceptions are swallowed, that surfaced as a confident, wrong number
  rather than an error.

- **Harvesting into tasks needs a far higher bar than displaying asks.** The
  Inbox card can afford loose extraction: a noisy line sits beside the mail that
  produced it. A task list cannot - a suggestion claims something is owed. The
  first build proposed "Please do not respond." as a task. Bulk mail is now
  skipped on List-Unsubscribe/List-Id/Precedence, and the cue list for tasks is
  much narrower than the one the card shows. Real inbox: 8 junk items to 0.

- **All-day entries are not meetings.** Counting them together produced
  "2 meetings (first at All day)" and "Tomorrow opens with X at 00:00".
