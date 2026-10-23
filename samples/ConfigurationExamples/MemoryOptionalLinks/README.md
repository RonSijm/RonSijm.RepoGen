# MemoryOptionalLinks: nullable target links with explicit query semantics

Start with [LinkedNotesRepositoryDesigner](Configuration/LinkedNotesRepositoryDesigner.cs).
The [EF context](Model/LinkedNoteContext.cs) gives each note a composite tenant/local ID and uses
an explicit CLR join entity with a surrogate fixture key and nullable LabelId. Persistence models
are internal and navigation collections are read-only. Public fixture rows explicitly map all
three tables; the consumer never constructs a persistence entity.

## This designer → these outputs

| Output | What to inspect |
| --- | --- |
| [EF-free contracts](../Contracts/Generated/LinkedNoteRepositories.g.cs) | Named Cards, PublicCards, Counts, RawCounts and Usage methods |
| [Production repositories](Persistence/Repositories.g.cs) | The exact shared EF projection/filter; no test-only query rewrite |
| [Fixture and test doubles](Generated/TestDoubles.g.cs) | Public seed stores, coordinated snapshots and directional requiredSource/requiredTarget flags |
| [Manifest](Generated/manifest.json) | Guarded optional-target receipts and explicit RawCounts RequiresOverride reason |

| Declaration | Effect |
| --- | --- |
| Public stores/maps on both endpoints and the link | EF/model-free arrangement of ordinary scalar data |
| `HasInMemoryNavigation(note => note.Labels)` | Explicit three-table relationship loading |
| `Labels.Where(label => label != null)` before projection/count | Nullable target links are deliberately excluded by the shared designer query |
| `PublicCards` adds `!label.Hidden` after its leading guard | Related filtering stays explicit; label-root policies are not injected |
| `HasInMemoryNavigation(label => label.Notes)` + Usage | The inverse has an optional source but required target; ordinary Count is safe |
| RawCounts + `RequireTestOverride` | SQL can count null-target links; the fake must not silently return a different count |

The [consumer](Program.cs) references Contracts, Testing and DI only. It arranges valid/null links,
checks composite source isolation, non-null counts and inverse/root visibility, explicitly scripts
the provider-bound count, mutates a detached result, changes a link and restores the fixture baseline.
No new setting is necessary; the existing store/query/navigation options remain off by default.

Nullable target links in unguarded projections or explicit Include/load profiles retain overrides.
Implicit/shared joins, SQL null propagation, provider constraints/comparisons and graph writes are
not inferred. See [nullable-link rules](../../../docs/implemented/testing/INMEMORY-OPTIONAL-LINKS.md). Shared tests also
compare actual generated SQLite queries and saved results against both supported fixture shapes.

Run: `dotnet run --project MemoryOptionalLinks.csproj`.
Regenerate/check with the [shared example workflow](../README.md#generate-check-and-run).
