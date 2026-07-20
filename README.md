# recap

A local-first developer chief of staff. `recap` reads your project's git
history and writes the things you'd otherwise write by hand: standup updates,
changelogs, branch summaries, and a list of loose ends. Everything is
generated deterministically from git on your machine. No server, no network
calls, no AI.

## Install

Requires git on PATH. Grab `recap.exe` from releases and put it on your PATH,
or build from source:

```
dotnet publish src/Recap -c Release -r win-x64
```

The output is a single self-contained native binary (Native AOT). Linux and
macOS builds work the same way with `-r linux-x64` / `-r osx-arm64`.

## Commands

```
recap init                          set your preferences (once)
recap standup                       what you did since your last standup
recap standup --since 2026-07-18    ...or since a date, ref, or "yesterday"
recap changelog                     release notes since the last tag
recap changelog --from v1.0 --to v1.1
recap summary feature/nets          what changed on a branch, and why
recap summary v1.0..HEAD            ...or for any range
recap loose-ends                    stale branches, unpushed commits,
                                    uncommitted work, TODO/FIXME markers
```

Global options: `--repo <path>` (default: current directory),
`--format md|text`, `--no-prompt`.

## How it adapts

First run of `recap init` asks four questions: format, verbosity, grouping,
tone. After that, every generated report ends with an
`[a]ccept / [e]dit / [r]eject` prompt (skipped when piping). Your actions
refine the stored preferences with plain rules, no ML:

- Edit a report down heavily and verbosity drops a step.
- Pad one out and verbosity rises.
- Reject the same report twice in a row and the grouping mode cycles.

Preferences live in `%APPDATA%/recap/prefs.json`. Delete the file to reset.

## The AI seam

All rendering flows through one interface, `IEnricher`
(`src/Recap/Enrich/IEnricher.cs`): structured facts in, prose out. v1 ships a
single deterministic implementation. A future AI-backed enricher can replace
it behind the same interface without touching git parsing, fact building, or
the CLI. That is the only extension point, by design.

## Development

```
dotnet test          unit tests plus integration tests that build a real
                     scratch git repo and run the full pipeline against it
dotnet run --project src/Recap -- standup --repo path/to/repo
```

Built with .NET 10 (current LTS), System.CommandLine, nullable reference
types on, warnings as errors.
