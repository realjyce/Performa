# Performa

Let yourself be managed. A helper, a summariser, a standup scribe, a
changelog keeper, a catcher of loose ends. An all-rounder by your side,
working from what your git history already knows.

`performa` reads your project's git history and writes the things you'd
otherwise write by hand: standups, changelogs, branch summaries, loose-end
reports. Everything is generated deterministically from git on your machine.
No server, no cloud, no AI. Just your commits.

## Install

Requires git on PATH. Grab `performa.exe` from releases and put it on your PATH,
or build from source:

```
dotnet publish src/Performa -c Release -r win-x64
```

The output is a single self-contained native binary (Native AOT). Linux and
macOS builds work the same way with `-r linux-x64` / `-r osx-arm64`.

## Commands

```
performa                               dashboard. With a workspace configured:
                                       every repo's today, loose ends, and your
                                       week's velocity in one screen
performa init                          set your preferences (once)
performa standup                       what you did since your last standup
performa standup --since 2026-07-18    ...or since a date, ref, or "yesterday"
performa changelog                     release notes since the last tag
performa changelog --from v1.0 --to v1.1
performa summary feature/nets          what changed on a branch, and why
performa summary v1.0..HEAD            ...or for any range
performa loose-ends                    stale branches, unpushed commits,
                                    uncommitted work, TODO/FIXME markers
```

Global options: `--repo <path>` (default: current directory),
`--format md|text|pretty`, `--no-prompt`.

Output has two faces: styled and colored when you're looking at a terminal,
plain markdown the moment it's piped or redirected. Paste into Slack or a
PR description and it's already clean.

## How it adapts

First run of `performa init` asks four questions plus an optional workspace
folder to scan for repositories (that powers the multi-repo dashboard). After that, every generated report ends with an
`[a]ccept / [e]dit / [r]eject` prompt (skipped when piping). Your actions
refine the stored preferences with plain rules, no ML:

- Edit a report down heavily and verbosity drops a step.
- Pad one out and verbosity rises.
- Reject the same report twice in a row and the grouping mode cycles.

Preferences live in `%APPDATA%/performa/prefs.json`. Delete the file to reset.

## The AI seam

All rendering flows through one interface, `IEnricher`
(`src/Performa/Enrich/IEnricher.cs`): structured facts in, prose out. v1 ships a
single deterministic implementation. A future AI-backed enricher can replace
it behind the same interface without touching git parsing, fact building, or
the CLI. That is the only extension point, by design.

## Desktop app

Performa also ships a desktop companion (`src/Performa.Desktop`, Avalonia) that
puts the same engine behind a UI: a multi-repo dashboard with velocity, a Daily
view (tasks, notes, today's commit timeline), a report generator, a workspace
loose-ends scan, a deterministic assistant, and honest "coming later" tiles for
the streams that plug into the same seam. Run it with:

```
dotnet run --project src/Performa.Desktop
```

The CLI and desktop share `Performa.Core`; neither touches the network.

## Development

```
dotnet test          unit tests plus integration tests that build a real
                     scratch git repo and run the full pipeline against it
dotnet run --project src/Performa -- standup --repo path/to/repo
```

Built with .NET 10 (current LTS), System.CommandLine, nullable reference
types on, warnings as errors.
