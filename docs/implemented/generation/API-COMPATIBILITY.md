# Reviewable closed-contract evolution

API baselines and per-contract files are optional generation features. They add no runtime service,
cache, resolver, or database dependency. Existing layouts and ordinary generation are unchanged.

## One file per generated interface

```csharp
var bundle = RepositoryGenerator.Generate(context, designer, RepositoryOutputLayout.PerContract);
var output = new RepositoryOutputLocations(
    "Persistence/Generated", Contracts: "Contracts/Generated",
    Manifest: "Persistence/Generated/manifest.json");
bundle.Write(output);
```

This keeps infrastructure/registrations shared and persistence files per entity, but emits each
generated `IReadXRepository`, `IWriteXRepository` and combined `IXRepository` into its own qualified
filename. Disabled capabilities produce no interface. Bind existing application ports as usual:
they remain application-owned source, not generated copies. Whole-model collision/signature
validation still happens before splitting. This layout requires closed-operation mode.

When changing layouts, `Check` reports obsolete context-owned `.g.cs` files (RGG0004). It never
deletes them. Review/remove just those generated files before compiling to avoid duplicate types.
The Ledger example demonstrates this layout without changing any method signatures.

## Capture a candidate, compare to an approved baseline

```csharp
var bundle = RepositoryGenerator.Generate(context, designer,
    RepositoryOutputLayout.PerContract).WithApiSnapshot();
var current = bundle.CreateApiSnapshot();
var baseline = RepositoryApiSnapshot.Parse(File.ReadAllText("ApiBaselines/MyApi.json"));
var differences = current.CompareTo(baseline);
if (differences.Any(change => change.IsBreaking))
    throw new InvalidOperationException("Review the reported API changes before generating.");

bundle.Write(new RepositoryOutputLocations(
    "Persistence/Generated", Contracts: "Contracts/Generated",
    Manifest: "Persistence/Generated/manifest.json",
    ApiSnapshot: "Persistence/Generated/RepositoryApi.json"));
```

`CreateApiSnapshot()` is in-memory and does not require an output file. `WithApiSnapshot()` explicitly
adds a candidate JSON file to the normal safe-write/check workflow. Its destination is a filename,
not a directory, in every layout. It is idempotent and never changes output unless requested.

For the first baseline, review the current candidate and explicitly copy/check it into a separate
`ApiBaselines` directory. To accept an intentional breaking change later, review the API diff and
update that baseline explicitly. Neither normal generation, `Check`, nor `CompareTo` does this for
you. The example runner never writes `Contracts/ApiBaselines/LedgerApi.json`, including during
regeneration; `--check` also fails if that initial baseline is missing.

Snapshots are layout-independent, sorted, versioned JSON. They record interface identity/base
contracts, CLR overload identity, return/parameter type annotations, parameter names, optional
arguments, and defaults. Configured bound-port methods use their actual application declaration,
including inherited methods and optional/default metadata, rather than the generator's lambda
parameter names. Transitive application-port base types are included for assignability checks.

## Include request and result data shapes

A method signature can stay identical while its request or result type changes. Opt in from the
designer to check that additional boundary:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Contracts.Generated")
    .IncludeApiDataContracts());
```

Generation still creates no API output unless the runner calls `WithApiSnapshot()` or the CLI
uses `--api-output`. `CreateApiSnapshot()` includes the configured data inventory in memory. No
runtime dependency, tracking policy, repository operation or generated interface changes. Disable
with `IncludeApiDataContracts(false)` when not needed. This option requires closed repositories;
simple and compatibility presets do not silently enable it.

The inventory follows **exposed operation types**: requests, final selected values, dictionary/
lookup keys and values, cursor values, saved command values, and supported inherited/default
port method signatures. It recursively follows public data members, constructor parameters,
application bases/interfaces, and generic arguments. Unexposed entities, anonymous query stages,
other registered projections and private/static members are not included merely because they
exist. Generic definitions are expanded once, so recursive `Node<List<T>>` shapes terminate.

For each application data type the snapshot records:

- Class/struct/interface/enum identity, abstract/sealed status and bases.
- Public non-indexed instance properties/fields, read/write nullable annotations (including
  `AllowNull`/`MaybeNull` information exposed by reflection), accessibility, init-only and required rules.
- Public constructors, CLR parameter identity, annotations, names, optional defaults and
  `SetsRequiredMembers`. Implicit parameterless struct construction is included.
- Enum names, invariant numeric values, underlying type and flags marker.
- Generic parameter order, CLR constraints/variance and available compiler nullable-constraint metadata.

Framework/Microsoft and RepoGen infrastructure assemblies are not expanded, but their generic
arguments are traversed: `Task<PagedResult<MyRow>>` still captures `MyRow`, and an `Optional<MyValue>`
input captures `MyValue`. Other public library value types can be included. Assembly-qualified
version strings are not stored. Ambiguous same-name application types and unsupported pointer/ref/
delegate data shapes are rejected rather than silently inventoried as equivalent.

Default snapshots remain the original **version 1**, byte-for-byte for unchanged APIs. Opt-in data
inventories use **version 2**; the parser supports both. Comparing a newly enabled inventory with a
version-1 baseline reports advisory RGA0010: review and explicitly approve that initial inventory.
An approved version-2 baseline cannot silently lose its checks: disabling the option produces
breaking RGA0004 in either compatibility mode. Existing Ledger baselines are not migrated or
accepted automatically. Library demonstrates a candidate with data shapes, without requiring
every example to have an approved baseline.

## Whose compatibility?

The default `ConsumersAndImplementers` mode checks callers **and** handwritten implementations,
mocks and fakes. Adding an abstract member can be harmless for callers yet break every fake that
implements that interface. New base contracts are conservatively reported as implementer changes.

```csharp
var callerChanges = current.CompareTo(baseline, RepositoryApiCompatibilityMode.Consumers);
```

Consumer-only mode allows new required operations/base contracts. Added overloads with an existing
name remain advisory: `null`/`default` call-site overload resolution can become ambiguous. Changed
optional defaults are also advisory because recompiled calls can behave differently. Review
advisories even when CI does not fail on them. Nullable annotation changes are conservatively
breaking in either mode, including annotations that trigger warnings-as-errors.

| ID | Report |
| --- | --- |
| RGA0001 | Invalid baseline JSON/schema, conflicting API metadata, missing baseline or unsafe baseline/output overlap |
| RGA0002 | Comparing a baseline for a different context |
| RGA0003 | Snapshot requested for the legacy selector surface |
| RGA0004 | Previously enabled data-contract checks disabled |
| RGA0005 | Invalid/ambiguous/unsupported data-contract metadata during capture/construction |
| RGA0010 | Initial data inventory needs explicit review (advisory) |
| RGA1001 | Contract removed/renamed |
| RGA1002 | Base contract removed |
| RGA1003 | Operation removed/renamed or input CLR signature changed |
| RGA1004 | Return/parameter type or nullable annotations changed |
| RGA1005 | Parameter renamed; named arguments can break |
| RGA1006 | Optional parameter became required |
| RGA1007 | New required operation/default implementation became abstract |
| RGA1008 | Optional default changed (advisory) |
| RGA1009 | Added overload needs call-site review (advisory) |
| RGA1010 | Added base contract can affect implementers |
| RGA1101 | Exposed data type removed/renamed/no longer reachable |
| RGA1102 | Data kind/base, abstract/sealed restrictions, enum representation or generic constraints changed |
| RGA1103 | Public data member removed/renamed |
| RGA1104 | Data member kind/type or read/write nullable annotations changed |
| RGA1105 | Data access removed or set changed to init-only/readonly |
| RGA1106 | New required member or constructor no longer satisfies required members |
| RGA1107 | Constructor removed or input CLR signature changed |
| RGA1108 | Constructor parameter annotation/name/optionality changed |
| RGA1109 | Enum name removed/renamed or numeric value changed |
| RGA1110 | New data-interface member/base can affect implementers |
| RGA1112 | New enum value requires handling review (advisory) |
| RGA1113 | Added non-required data member needs payload review (advisory) |
| RGA1114 | Constructor default changed (advisory) |
| RGA1115 | Constructor overload needs call-site review (advisory) |
| RGA1116 | Generic parameter name changed (advisory; member-type changes may also be reported) |

## CLI and CI

Configuration still lives in code. The CLI only adapts the engine's tooling options:

```powershell
repogen generate --project Models.csproj --designer-project Designers.csproj `
    --per-contract --output Persistence/Generated --contracts-output Contracts/Generated `
    --api-output Persistence/Generated/RepositoryApi.json `
    --api-baseline ApiBaselines/MyApi.json --check
```

Breaking changes exit **4 before any generated-file writes**, even without `--check`. A missing
or malformed baseline fails rather than disabling comparison. `--validate` can also compare a
baseline without output writes. `--api-consumers-only` explicitly selects caller-only comparison;
it requires `--api-baseline`. No resolved generated output (including the manifest) may overlap
the approved baseline file. Output parent directories and baseline paths remain trusted inputs.

The opt-in build target can call an independently built runner that checks both freshness and
API compatibility. Ledger supplies that example. Simple/Configured/Reports/Library do not acquire
baseline requirements or extra output files.

## Deliberate limits

This is a conservative source-contract evolution guard, not a proof of full CLR binary or JSON/wire
compatibility. Data shapes are checked only when opted in. Serialization attributes/naming rules,
indexers, static members, ordinary methods/operators/events, generic/default-method bodies on
external ports, SQL, visibility/loading behavior and operation semantics are outside this inventory.
Compiler annotations stripped by another library cannot be recovered. Data-interface additions
are conservatively implementer-breaking, even if a particular member has a default body.

Operation annotations come from the generated/validated declarations; data-member annotations
come from the compiled application DTO/request declarations. Designer result annotations do not
rewrite a DTO's members. Return variance and full call-site overload resolution are not analyzed
with an entire consumer compilation. Framework/dependency APIs are not completely compared.
Compile consumers and run integration tests alongside the baseline check. Baseline files are
trusted, reviewable inputs; neither snapshots nor manifests are authorization or migration rules.
