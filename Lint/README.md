# Lint

Self-contained, portable formatting configuration and scripts. Everything needed to reproduce
this repo's exact code style lives in this one folder - see [Portability](#portability) to reuse
it in another repo.

## Contents

| File | Purpose |
| --- | --- |
| `CodeStyle.DotSettings` | Custom `jb cleanupcode` "GoldenExample" profile - the structural formatting rules (braces, initializers, empty blocks, attribute placement). Passed explicitly via `--settings`, so it can live anywhere with any name. |
| `.editorconfig` | Canonical copy of the repo's EditorConfig (indentation, brace style, using-directive rules, etc.). **Must also be deployed to the repo root** - see `install.ps1`. |
| `format.ps1` / `format.bat` | Runs the actual reformatting (or `verify` check). |
| `install.ps1` / `install.bat` | One-time setup: deploys `.editorconfig` to the repo root. |

## Usage

```bat
Lint\format.bat            REM reformat the auto-detected solution in the repo root
Lint\format.bat verify     REM check only, fails if formatting is needed (no files touched)
```

Or directly via PowerShell:

```powershell
.\Lint\format.ps1
.\Lint\format.ps1 -Verify
.\Lint\format.ps1 -Solution path\to\Specific.slnx   # override auto-detection
```

## What runs

1. **`jb cleanupcode`** (JetBrains ReSharper Command Line Tools - free, install with
   `dotnet tool install -g JetBrains.ReSharper.GlobalTools`) using the custom **"GoldenExample"**
   cleanup profile defined in `CodeStyle.DotSettings`. This profile runs *only* code reformatting
   (braces, initializers, empty blocks, attribute placement) - unlike ReSharper's default
   "Full Cleanup" profile, it does **not** remove "redundant" braces from single-statement
   `if`/`for`/`while` bodies, optimize/remove `using` directives, touch `var`-vs-explicit-type
   usage, or rewrite file headers. If `jb` isn't installed, this step is skipped with a warning.
2. **`dotnet format whitespace`** (SDK-bundled) - a final indentation/whitespace-only pass.

`format.bat verify` / `.\Lint\format.ps1 -Verify` only runs step 2 with `--verify-no-changes`,
since `jb cleanupcode` has no built-in "check only" mode.

## Portability

This folder is designed to be copied wholesale into another repo:

1. Copy the entire `Lint` folder into the target repo (same relative location: repo root).
2. From the target repo root, run `.\Lint\install.ps1` (or `Lint\install.bat`) once. This deploys
   `Lint\.editorconfig` to the repo root as `.editorconfig` - required because EditorConfig files
   are discovered purely by directory hierarchy (walking up from the file being formatted), so
   there's no way to point tooling at `Lint\.editorconfig` directly the way `--settings` does for
   `CodeStyle.DotSettings`.
3. Use `.\Lint\format.bat` / `.\Lint\format.bat verify` there exactly as in this repo. The
   solution file is auto-detected (there must be exactly one `.slnx`/`.sln` in the repo root, or
   pass `-Solution` explicitly).

Re-run `install.ps1` any time `Lint\.editorconfig` changes, to keep the deployed root copy in
sync.

### Known limitation

Neither tool will collapse an already hand-wrapped multi-line member declaration (e.g. a primary
constructor with one parameter per line) back onto a single line, even with every
"keep existing arrangement" setting disabled - both formatters only *add* line breaks where
required, they never *remove* an author's existing ones. Fix such cases manually if you want them
on a single line.

## Why not the `dotnet-format` global tool?

`dotnet tool install -g dotnet-format` installs the last-ever-published standalone release
(`5.1.250801`). That version corrupts indentation on classes combining a primary constructor with
a generic interface base list (e.g. `class Foo(Bar bar) : IEndpoint<Baz>`), which this codebase
uses throughout. The SDK-bundled `dotnet format` command (available via a modern .NET SDK, no
separate install needed) handles this correctly, so these scripts call `dotnet format whitespace`
instead.

## Why not CSharpier?

CSharpier is a Prettier-style opinionated reformatter with very few configuration knobs
(`printWidth`, `useTabs`, `indentSize`, `endOfLine`) - you get its style or nothing, with no way
to keep this repo's Allman braces, brace/attribute placement rules, etc. `jb cleanupcode` was
chosen instead because it exposes fine-grained ReSharper formatting rules via `.DotSettings`,
letting this repo's exact style be captured and enforced explicitly rather than adopting a
third-party opinion.
