# Optional compiler checks and the closed-contract boundary

RepoGen's normal generation validates the designer and renders C# without compiling it. The
optional `RonSijm.RepoGen.Validation` library adds a real, in-memory C# compilation before you
choose to write outputs. It is a design-time dependency, not a generated application dependency.
Nothing is enabled automatically, and the standalone CLI does not acquire Roslyn through this feature.

## Code-first engine without the executable

Reference `RonSijm.RepoGen.Generation` in an independent generation runner. It contains the same
engine used by the `repogen` executable, as an ordinary library rather than a dotnet-tool package.
Its existing public API namespace remains `RonSijm.RepoGen.Tool`, so existing `using` statements
and source callers remain valid. The Tool assembly forwards its earlier public engine/API types.

The library and optional validator target .NET 10, matching the design-time host. Your generated
application can still target .NET 8; it references neither library. The engine does not depend on
the executable, Roslyn, a SQL Server provider, or EF's in-memory provider. The CLI adapter still
owns project builds, assembly loading, invocation parsing and console output. Engine source files
are linked into the library, not duplicated, while retaining their existing repository paths.

## Check a bundle before writing

```csharp
using RonSijm.RepoGen.Tool;
using RonSijm.RepoGen.Validation;

var bundle = RepositoryGenerator.Generate(context, new MyRepositoryDesigner());
var options = new RepositoryCompilationOptions(
    references: File.ReadAllLines("inputs/persistence-references.txt"),
    contractReferences: File.ReadAllLines("inputs/contract-references.txt"),
    languageVersion: "12",
    warningsAsErrors: true);

var report = bundle.ValidateCompilation(options, cancellationToken);
foreach (var diagnostic in report.Diagnostics)
    Console.Error.WriteLine(
        $"{diagnostic.Stage}: {diagnostic.File}:{diagnostic.Line}:{diagnostic.Column}: " +
        $"{diagnostic.Id}: {diagnostic.Message}");

report.ThrowIfFailed(); // RepositoryCompilationException retains the immutable report.
bundle.Write(output);  // Still explicit; validation never calls Write.
```

The reference lists are explicit paths to the **target's** framework and application/dependency
assemblies. Do not use the generation host's loaded assemblies as a target-framework substitute.
The validator does not restore/build projects, discover missing assemblies, run analyzers/source
generators, connect to a database, load the emitted assembly, or execute generated/consumer code.
Compiler output lives only in memory. Ordinary output freshness, ownership and API-baseline checks
remain separate and should be used alongside this check.

Provide independently buildable model/manual DTO/port inputs, plus the runtime/provider assemblies
used by the generated implementation. Avoid references containing earlier generated facades when
compiling replacement facades: those can cause legitimate source-versus-metadata shadowing warnings.
Bootstrap inputs without generated persistence/facades first.

If `contractReferences` is supplied and the bundle has contracts, the validator first compiles
those files separately, then compiles all generated C# together with `references`. Supply only
the target framework, Abstractions and manual contract/DTO inputs for the independent contract
check; the option does not silently filter a supplied reference list. `ContractsChecked` reports
whether that separate compilation was attempted. Passing no contract list checks implementation
C# only. Non-contract selector modes never claim an EF-free contract compilation.

If the designer enables [scripted test doubles](../testing/SCRIPTED-TEST-DOUBLES.md), the implementation
reference list must also contain the optional Testing runtime and DI abstractions. To check their
boundary separately, pass `testDoubleReferences` with only target framework, Abstractions, Testing,
DI abstractions and manual DTO/port inputs. Generated contracts and testing files compile together
under stage `TestDoubles`; `TestDoublesChecked` is true only when that separate compilation was
attempted. Like the contract stage, this excludes arbitrary additional implementation/consumer
sources and does not auto-filter references. No testing output means no testing-stage attempt.
For [shared fixture stores](../testing/INMEMORY-FIXTURES.md), include the explicitly selected row/key input
assemblies too. An entity-shaped store needs its entity assembly; a public data proxy can keep
database models and EF out of the separate testing reference set. Contracts themselves are unchanged.

`ValidateCompilation` also accepts a `RepositoryGenerationBatch` and compiles its contexts together,
checking actual cross-context source references. Virtual diagnostic filenames contain the context
and generated filename, keeping split/multi-context output distinguishable. JSON manifests and API
snapshots are not parsed as C#.

## Compiler policy and consumer checks

Options are copied into read-only collections. The default C# version is the packaged compiler's
`latest`, not a claim about every future language version; set it to your application's version.
Nullable analysis is enabled and the generated files retain their own nullable directives.

Warnings remain visible and are non-blocking unless `warningsAsErrors: true` is selected. Like the
.NET SDK's `Microsoft.CSharp.Core.targets`, the default suppresses only `CS1701`/`CS1702` assembly
unification warnings. Pass `suppressedDiagnosticIds: []` for stricter assembly-identity reporting,
or an explicit compiler-ID list to match a reviewed `NoWarn` policy. Compiler errors cannot be
hidden by that warning policy; reference/configuration failures cannot be suppressed. Reference
paths are normalized/deduplicated, but different files with the same assembly name are rejected
instead of arbitrarily picking a target version.

You can add a small consumer compile fixture without executing it:

```csharp
var options = new RepositoryCompilationOptions(targetReferences,
    additionalSources: [new RepositoryCompilationSource("ReadConsumer.cs", """
        public static class ReadConsumer
        {
            public static void Use(My.Contracts.IReadOrderRepository repository)
            {
                _ = repository.SaveChangesAsync(); // Should fail: no write capability on this port.
            }
        }
        """)]);
```

Reports preserve ordinary `CS...` IDs, severity, phase, invariant-culture messages and one-based
source coordinates. Cancellation throws rather than returning a successful/partial report.

| Diagnostic | Meaning |
| --- | --- |
| RGV1001 | Missing, unreadable, empty or invalid explicit reference input |
| RGV1002 | Different reference files supply the same assembly name |
| RGV1003 | Invalid source names/content, language version or diagnostic policy |
| RGV1004 | `ThrowIfFailed()` rejected an unsuccessful report; inspect its diagnostics |

## Opt-in MSBuild reference export

Explicitly import `BuildSettings/RepoGen.CompilationReferences.targets` into an independently
buildable input project. A normal build does nothing. To export the resolved compile references:

```powershell
dotnet build My.GenerationInputs.csproj -c Release -p:RepoGenExportCompilationReferences=true
```

By default the UTF-8 list is `RepoGen.CompilationReferences.txt` beside that project's output DLL.
Set `RepoGenCompilationReferencesFile` to an explicit file to change it. The opt-in target writes
only that reference-list artifact, never repository source, and skips IDE design-time builds.
Re-export after changing dependencies/framework/configuration. It is not an installed automatic
NuGet build target. The list is an input inventory, not a guarantee that every implementation or
handwritten contract assembly has been added; include the independently built model/DTO assembly
itself when it is not among the input project's references.

The examples demonstrate a .NET 10 runner validating .NET 8 output:

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
```

The optional `GenerationInputs` project resolves the examples' .NET 8 framework/runtime/model/DTO
references without generated persistence. The script bootstraps facade-free inputs, compiles all
twelve configurations (including separate EF-free contracts and the multi-context batch), checks
freshness, then normally builds/runs the applications. All model/API/compiler checks complete
before the runner publishes any example output. Compiler failure exits 5, with no generated writes;
stale checks still exit 3 and breaking API baselines exit 4. Omitting `-ValidateCompilation` retains
the ordinary workflow and does not export inputs or perform these compiler checks.

## Stronger normal closed-contract validation

Closed mode already promises application-facing types. Its existing `RGD1502` check now follows
public data members, indexer inputs, constructors, bases, interfaces, helper/operator and event
callback signatures, and generic
constraints, in addition to every closed generic argument. It rejects mapped entities, EF core/
relational/provider types, RepoGen infrastructure/design-time types, query/delegate escape shapes
and ADO connection/command/transaction/reader handles. Errors include the offending public path.
Putting a user class in a `System.*` namespace does not make its members trusted framework leaves.

Each application generic definition is expanded once. A valid `Node<T>` containing
`Node<List<T>>` is bounded, while a later `Node<DbContext>` is still rejected because its closed
argument is checked independently. Framework containers and ordinary unmapped POCOs remain usable;
strong IDs can retain span/`out` parsing helpers and ordinary DTO notification events without
gaining an ORM escape hatch. Validation
never invokes application constructors, getters or helper bodies.

These checks apply only to active closed operations and bound ports. Compatibility/CQRS selector
surfaces intentionally use model types and do not acquire closed-contract restrictions. No new
runtime API or mandatory designer option is added. This is static surface validation, not proof
of a DTO assembly's entire dependency graph, method-body behavior, wire format or authorization;
opaque `object` values cannot be semantically proven by reflection. SQL translation, lifetime,
transaction and cache guarantees still need their relational execution tests.
