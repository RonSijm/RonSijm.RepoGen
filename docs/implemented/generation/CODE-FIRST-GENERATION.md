# Code-first generation and CI checks

The standalone `repogen` executable is an adapter over the same public `RepositoryGenerator`
engine in the referenceable `RonSijm.RepoGen.Generation` library. An independent C# runner can call it
without referencing/installing the executable. Existing API namespaces remain `RonSijm.RepoGen.Tool`;
Tool forwards its earlier public types. Advanced configuration stays in a RepositoryDesigner;
there is no need to encode it in more CLI flags.

```csharp
using var context = new MyContext(new DbContextOptionsBuilder<MyContext>()
    .UseSqlite("Data Source=:memory:").Options);

var bundle = RepositoryGenerator.Generate(context, new MyRepositoryDesigner());
var output = new RepositoryOutputLocations(
    "Persistence/Generated/Repositories.g.cs",
    Contracts: "Contracts/Generated/Repositories.g.cs",
    Manifest: "Persistence/Generated/manifest.json");

bundle.Write(output); // Explicit generation; only replace generator-owned files.
// Or: bundle.Check(output); // Differences only; no output/directory writes.
```

The engine reads the configured EF model; it does not connect to/create a database. Supply the actual
design-time provider when using provider-specific metadata/capabilities. The caller owns the context.
The engine/Tool/runner target .NET 10 to load supported input assemblies; application/runtime projects can
target .NET 8 and need not reference the generator executable.

`Generate(context, RepositoryDesign)` supports a built model or no designer. Generation returns an
immutable bundle of C#/manifest files. It validates the whole model/contracts before splitting.

## Layout and ownership

`RepositoryOutputLayout.SingleFile` preserves the existing output layout. `PerEntity` produces shared
infrastructure/registration once, then separate entity repository/operation and contract/projection
files. `PerContract` additionally gives each generated closed read/write/combined interface its
own file; application-owned bound ports remain handwritten. For split output, C# destinations are
directories; manifests and optional API snapshots remain explicit filenames.

Opt-in [scripted test doubles](../testing/SCRIPTED-TEST-DOUBLES.md) use `RepositoryOutputLocations.TestDoubles`:
a separate test file in SingleFile mode or directory in split modes. They share ownership, freshness
and collision checks. Configuration stays in the designer; the CLI only supplies a
`--test-doubles-output` destination. Defaults produce no test output or testing dependency.
Optional [shared fixture stores](../testing/INMEMORY-FIXTURES.md) are emitted into that same testing output;
their designer selects public row/key/copy adapters, not another set of CLI flags.

For several contexts in one persistence assembly, optionally configure `UseGeneratedNamespace`
in each designer and use `RepositoryGenerator.GenerateMany(...)`. It validates cross-context CLR
types/port registrations/paths and preflights the whole batch before writing. Explicitly isolated
split infrastructure filenames can share an output directory. See
[Multi-context generation](MULTI-CONTEXT-GENERATION.md) for shared entities, independent contexts,
cache isolation and example code. Single-context defaults are unchanged.

`Write` preflights all resolved destinations/collisions/handwritten-file ownership before modifying
any output. C# uses the auto-generated marker; manifests use RepoGen's generator/inventory marker.
Existing linked output files are refused. Missing parent directories are created only by `Write`.
Each replacement is atomic at the file level, not a multi-file filesystem transaction.

Generated C# records its context, allowing `Check` to report obsolete files for that context after
split configuration changes. Obsolete files are never deleted automatically. Review/remove only
those generated files explicitly; never sweep a directory containing handwritten code. New output
directories should be application-controlled, not untrusted symlink/junction trees.

| Diagnostic | Meaning |
| --- | --- |
| RGG0001 | Generated output missing/stale |
| RGG0002 | Missing/invalid/colliding output destination or linked file |
| RGG0003 | Refusal to overwrite handwritten output |
| RGG0004 | Obsolete context-owned split file; manual review/removal required |
| RGG0005 | Opt-in build check has no configured command |
| RGG0006 | PerContract layout requires closed repositories |
| RGG0007 | Colliding generated top-level type, including across contexts |
| RGG0008 | Refusal to replace another context's generated output |
| RGG0009 | Competing cross-context application-port bindings |
| RGG0010 | Duplicate context type in a generation batch |

Other model diagnostics remain descriptive validation exceptions; there is not a promise of stable
IDs for every designer error. Optional API baselines now report stable RGA diagnostics for generated
closed-contract evolution; see [API compatibility](API-COMPATIBILITY.md) for coverage and limits.
The designer's optional `IncludeApiDataContracts()` extends those snapshots to exposed request/
result data members and construction rules. It does not create an output automatically.
Common configuration failures have [stable diagnostic IDs](DESIGNER-DIAGNOSTICS.md).

## Avoid the first-generation build cycle

Keep separate inputs:

- Models: EF contexts/entities, optionally manual mapper/DTO contracts.
- Contracts: manual requests/DTOs/application ports plus generated EF-free contracts.
- Designers: model/contract/designer references, no generated persistence dependency.
- GenerationRunner: Generation, Models, Designers and selected design-time provider packages; Validation is optional.
- Persistence/consumer: generated implementation and runtime references, not needed to build the runner.

Do not make manual DTO inputs depend on generated repository interfaces during bootstrap. The
[complete runner](../../../samples/ConfigurationExamples/GenerationRunner/Program.cs) generates the
configuration examples, including split advanced Ledger output and a two-context generation batch.
`Generate.ps1` builds that runner first, generates/checks, then builds/runs consumers.

The example Contracts project also supports `-p:RepoGenBootstrap=true`, which excludes its generated
facades during the runner build. This verifies that only manual request/DTO/port inputs are needed
for first generation; a normal consumer build does not use that switch.

## Explicit build/CI integration

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
```

`-Check` fails if outputs differ and never regenerates them. Direct runner checks exit 3 on drift;
bad invocation exits 2. Breaking API-baseline changes exit 4 before writing. The CLI offers the same
`generate --check`, plus `--per-entity` / `--per-contract` layouts and optional `--api-output` /
`--api-baseline`. `--validate` checks the design (and any supplied baseline), not output freshness.

Optional `RonSijm.RepoGen.Validation` compiler checks emit only to memory, with explicit target
references, independent contract compilation and structured diagnostics. The example's
`-ValidateCompilation` bootstraps .NET 8 reference inputs and gates all generated writes; compiler
failure exits 5. Normal generation/CLI and generated applications have no Roslyn dependency.
See [Compiler validation](COMPILATION-VALIDATION.md) for C# usage and opt-in MSBuild reference export.

`BuildSettings/RepoGen.Check.targets` is an optional, explicitly imported pre-compile check. It is
disabled by default and skipped for IDE design-time builds. Set `RepoGenCheckOnBuild=true` and a
trusted `RepoGenCheckCommand` invoking an independently built runner in `--check` mode; set its
working directory if needed. It does not secretly build/generate the runner or rewrite source.

The Ledger project demonstrates it:

```powershell
# Build the independent runner first; generated persistence is not involved.
dotnet build samples/ConfigurationExamples/GenerationRunner -c Release
dotnet build samples/ConfigurationExamples/Ledger -c Release -p:RepoGenCheckOnBuild=true
```

That import is an in-repository opt-in example, not an automatically installed NuGet build target.
Consumers can copy/import it or run the check explicitly in CI. Checked-in outputs allow normal
simple builds without a design-time generation step or a .NET 10 runtime in the deployed application.
