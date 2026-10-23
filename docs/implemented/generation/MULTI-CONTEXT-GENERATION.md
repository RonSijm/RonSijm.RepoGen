# Generate several contexts into one application

`UseGeneratedNamespace(...)` and the code-first `GenerateMany(...)` workflow are optional. Ordinary
single-context output is unchanged: repositories/selectors stay beside their entities, projection
helpers retain their existing placement, and common infrastructure stays in `RonSijm.RepoGen`.
There is no new runtime dependency or CLI configuration language.

## Isolate generated persistence, not your models

Configure each designer independently:

```csharp
// Primary persistence: small CQRS selectors over an existing scoped context.
modelBuilder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors()
    .UseGeneratedNamespace("MyApp.Persistence.Primary"));

// A second designer: read-only named operations behind an EF-free application port.
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseGeneratedNamespace("MyApp.Persistence.Archive")
    .UseClosedRepositories("MyApp.Contracts.Archive"));
```

The setting moves generated repository interfaces/implementations, selectors and their base types,
legacy paging/cursor/group helpers, query/projection extensions, cache/resolver interfaces and CQRS
DI registration. It does not rename EF entity classes, DTOs, application-owned ports or provider/
runtime types. Closed contract output and its DI registration remain in `UseClosedRepositories`'s
explicit namespace; their page/cursor types remain in the EF-free Abstractions library.

This supports the **same entity CLR type in multiple contexts** without duplicate persistence
types. Use distinct closed contract namespaces too when their entity-derived facade names overlap.
Public selector paging types are intentionally context-specific; use closed contracts when page
results must cross a persistence-free application boundary.

Presets preserve the explicit namespace. `UseGeneratedNamespace(null)` restores default placement.
Changing placement requires regeneration/rebuilding and is a source API change for consumers of
the old persistence names. It need not change an existing application-owned closed port or snapshot.
Namespace-isolated selectors must be tool-generated rather than reused from an unrelated selector
core. The public engine already defaults `generateSelectors` to true.

If importing two namespaces makes short interface names ambiguous, use a C# alias or the fully
qualified name. Registration classes can similarly be called explicitly, particularly when two
context classes have the same short name. This is ordinary C# namespace resolution, not a runtime
context-selection mechanism.

## Generate/check/write as one batch

```csharp
using var primary = CreatePrimaryDesignTimeContext();
using var archive = CreateArchiveDesignTimeContext();
var batch = RepositoryGenerator.GenerateMany(
    new RepositoryGenerationRequest(primary,
        new RepositoryOutputLocations("Persistence/Generated/Primary.g.cs",
            Projections: "Persistence/Generated/PrimaryQueries.g.cs",
            Manifest: "Persistence/Generated/Primary.json"),
        new PrimaryRepositoryDesigner().CreateModel()),
    new RepositoryGenerationRequest(archive,
        new RepositoryOutputLocations("Persistence/Generated/Archive.g.cs",
            Contracts: "Contracts/Generated/Archive.g.cs",
            Manifest: "Persistence/Generated/Archive.json",
            ApiSnapshot: "Persistence/Generated/ArchiveApi.json"),
        new ArchiveRepositoryDesigner().CreateModel(), IncludeApiSnapshot: true));

var differences = batch.Check(); // No output/directory writes.
var changedPaths = batch.Write(); // Explicit; preflight every entry before writing any entry.
```

GenerateMany validates generated top-level CLR names (including internal helpers and generic
arity), competing bound-port registrations, duplicate context types, and all resolved output paths.
Contexts are ordered by CLR name independently of caller order; explicit designer module ordering
is unchanged. Each request retains its own surface, feature switches, provider, layout and optional
snapshot. The caller continues to own all design-time contexts.

Split layouts can share a directory: with explicit placement, infrastructure filenames include the
generated namespace and context name, and entity/helper/contract files retain qualified names.
Manifests and API snapshots are still explicit, distinct filenames. No context's configuration is
merged into another's; there is no least-common-denominator shared selector base.

`Write` checks all file ownership, existing-file parents and destinations before the first output
replacement. It rejects a generated file/manifest/snapshot marked as owned by another context,
as well as handwritten or linked files. Existing unmarked legacy generated C# remains migratable.
Files replace atomically one at a time, **not as a multi-file filesystem transaction**. Controlled
output directories are required; concurrent writers/untrusted directory junction trees are not
a supported coordination mechanism. Obsolete split files are reported per context and never deleted.
The same ownership/parent checks also protect single-context writes.

An optional API snapshot is a review candidate, not automatic baseline approval. Apply any desired
baseline comparisons to the bundles before calling Write; GenerateMany does not accept baselines.

## Independent runtime boundaries

Generated DI ports resolve their own configured context. Read-only defaults, staging and saves
remain per context. Saving the primary database does not save the archive or establish a distributed
transaction. Shared unit-of-work behavior exists only for repositories sharing the same context.
Queries still cannot federate databases. Use the existing keyed lease/resolver strategy for selecting
one context's tenant/database per request; batch generation is not a tenancy policy.

With namespace isolation, legacy cache keys/tags include the context identity so repositories
sharing an entity type do not share rows or invalidate another database's cache entries. Each
generated legacy cache interface moves with its namespace; a shared backend can implement/adapt
those interfaces. Closed caching retains its explicit scope/visibility partition requirement;
namespace placement does not replace that requirement or supply distributed cache durability.
If two different contexts deliberately share the same physical data, coordinate their cross-context
invalidation explicitly. A CLR context identity also cannot distinguish tenant/visibility partitions
within one context type; legacy backends still need that application-owned scope policy.

## Diagnostics and example

| ID | Meaning |
| --- | --- |
| RGD1801 | Invalid/reserved generated namespace |
| RGD1802 | Isolated namespace would reuse existing selectors rather than generate its own core |
| RGG0002 | Missing/colliding output path, file-as-parent, directory destination or linked file |
| RGG0007 | Generated top-level CLR type collision; select distinct persistence/contract namespaces |
| RGG0008 | Refusal to overwrite another context's generated output |
| RGG0009 | Two contexts bind the same application port; generated DI cannot choose one |
| RGG0010 | A context CLR type occurs more than once in the batch |

Batch checks are fail-fast configuration checks, not compilation against every handwritten type
in the target assembly or proof of SQL translation. Final consumer compilation/provider tests remain
necessary. Different same-named entities within one designer still need distinct generated names;
an explicit namespace does not invent domain-specific renaming rules.

The runnable [MultiContext example](../../../samples/ConfigurationExamples/MultiContext/Program.cs) uses
two in-memory SQLite databases with the same `StoredNote` model and key. One designer emits simple
CQRS selectors; the other binds a closed EF-free archive port. Both generate into the same persistence
directory/assembly with different split layouts. Its service uses named reads, writes only to the
primary context, and checks that the archive remains independent. The other nine example outputs
remain unchanged.
