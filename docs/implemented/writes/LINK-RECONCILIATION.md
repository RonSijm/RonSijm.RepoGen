# Optional explicit join-row reconciliation

`HasCommand(...).ReconcileLinks(...)` adds designer-defined many-to-many **writes through an
explicit ordinary join-row collection**. It reuses bounded tracked reconciliation; it does not
turn a related entity into an aggregate child or mutate an arbitrary skip-navigation graph.

Start with [BoardLabelsRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryLinks/Configuration/BoardLabelsRepositoryDesigner.cs)
and its [exact output map](../../../samples/ConfigurationExamples/MemoryLinks/README.md).
Unconfigured/simple repositories retain their existing output and dependencies.

## Configure the operation once

```csharp
entity.HasCommand("ReplaceLabels")
    .Where<ChangeLabels>((board, request) => board.Id == request.BoardId)
    .ReconcileLinks<BoardLabel, LabelInput, int, ChangeLabels>(
        board => board.Links, request => request.Labels,
        link => link.LabelId, input => input.LabelId,
        links => links.MaximumItems(100)
            .Add(input => new BoardLabel { LabelId = input.LabelId, Caption = input.Caption })
            .Update(setters => setters.SetProperty(link => link.Caption,
                (link, input) => input.Caption))
            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
    .Save();
```

The service calls `ReplaceLabelsAsync(request)`. It does not instantiate EF entities or provide
runtime selectors, setters or reflection-based field names. `ReconcileLinks` chooses tracked Patch
when no kind is set. It uses the same collection-configuration builder because bounds, Add/Update,
Keep/Reject/Delete, When, version expectations and write permissions have the same explicit semantics.

Add is disabled by default; missing keys are kept by default; Update is disabled until configured.
Add + Keep provides linking/merge, Update changes payload, and Delete removes missing visible join rows.
An empty Delete request removes visible links, not the related entity table. Explicit Save/Stage is
required. Multiple link/ordinary leaf collections may compose in one command.

## Two FK roles, complete immutable reference keys

The navigation must select a direct principal-to-dependent `ICollection<TLink>` such as Board.Links,
not Board.Labels. The EF model supplies the selected owner FK and the other target FK. A skip
navigation is not required: an explicitly mapped join-only model can use the same two-FK contract.

By default, generation requires an ordinary keyed leaf join with exactly two required, non-unique many-to-one,
non-ownership FKs. Their columns are disjoint by default; [AllowSharedReferenceColumns](SHARED-REFERENCE-COLUMNS.md)
explicitly permits compatible shared columns with complete-key owner-consistency checks.
Target ownership/inheritance/shared models are excluded.
[TargetReference and AdditionalReference](ADDITIONAL-LINK-REFERENCES.md) separately enable explicitly
accounted-for required non-owner FKs, with complete scalar initialization on new links and immutable matches.
[OptionalAdditionalReference](OPTIONAL-ADDITIONAL-REFERENCES.md) separately admits nullable extra FKs,
with explicit all-or-none creation values; owner and matching target remain required.
There must be an EF key or unfiltered unique index proving the complete link combination unique
globally or within the selected owner. A separate surrogate/generated join ID is allowed in production.

Matching selects the **complete target FK**, in metadata order, as one scalar or a two-to-seven-part
ValueTuple. Complete composite/alternate principal keys retain their declared order. Null tuple
components, duplicate keys, null inputs/items and excessive inputs fail before context acquisition.
Inputs are bounded, enumerated once and cancellation-aware; values are not deep-cloned.

Creation must explicitly assign every target component and approved payload scalars. Ownership is
bound from the selected parent's complete principal key, never from the mapper/request. Updates cannot
change owner columns, target columns or matching/primary keys—even with AllowWrite. Change a link
identity through explicit removal/addition. Ordinary `ReconcileCollection` still forbids FK matching
and FK mapping; the link opt-in does not weaken its protection.

No target is created, updated or deleted by these rules. Runtime creation also rejects populated
navigation graphs, including graphs originating in constructors. Target existence is enforced by
the production FK constraint; it is not an automatic target fetch or authorization check.

## Scope, outcomes and shared persistence

Root policies protect parent selection; join EF filters and configured join visibility protect the
bounded mutation query. Hidden/fixup-only join rows are not removed as missing. Target repository
visibility is **not implicitly injected** into link authorization or mutation scope. Authorize permitted
references in the application/designer explicitly; a FK or read-policy review is not authorization.
Unknown hidden keys can still collide with a database unique constraint.

When skips omitted collections entirely. Supplied null is invalid, not delete-all. Empty input follows
the selected missing policy. Parent NotFound/expected-version Conflict and link-version Conflict retain
existing command outcomes. Parent aggregate versions require an unconditional parent setter; all
aggregate writers must cooperate. Matched link versions require an unconditional payload/token setter.
Missing-link deletions use their loaded original token; guard stale full replacement with the parent
version rather than invent a token for an omitted item.

Stage changes only tracked/pending state. Saving uses one **shared SaveChanges** call and includes
earlier pending work, even changes to a related entity made outside this command. It is not a single
SQL statement/network-roundtrip or a repository-scoped save. Operation-owned leases cannot Stage.
Saved link commands require relational automatic transactions and enabled/supported savepoints in
an existing transaction; unsafe/ambient save boundaries are rejected before mutation. Fully omitted
collection-only commands do not require a save boundary because they do not save.

Before-save/save failures restore command payload/membership/states without discarding earlier pending
work. Generated join keys are detected while still tracked before failed additions are detached, so
retries cannot encounter a rolled-back identity-map entry when automatic detection is disabled.
EF fixup on pretracked endpoints is covered by SQLite regression cases; no internal EF APIs are called.
Lazy loading stays disabled during the bounded command and its exact prior flag is restored.

Use ReturnsSaved for a fresh complete-parent-key read after saving, including explicitly declared
[many-to-many fixture reads](../testing/INMEMORY-MANY-TO-MANY.md). The result read is separate, not atomic returning;
post-save failures retain RepositorySavedResultException semantics. Related visibility in that result
is an explicit projection rule. Join cache tags/dependent query owners participate in the existing
commit-aware invalidation machinery.

## Optional fixture implementation

Use existing closed test output, public/entity-shaped stores, scalar read/write maps and
UseInMemoryTestCommands, then opt the command into `SimulateCollectionWrites()`. The fixture reuses
the same owner binding, complete target matching, payload setters, versions, limits, missing policy,
Stage/shared Save, checkpoint rollback, scripts and fresh saved-result rules. No additional global
option or test-specific service DSL is added. Fully mapped public rows support internal/read-only
persistence models without constructing those entities.

Only the join store is written. Target stores are needed when a designed read/saved DTO traverses
them, not merely to stage FK scalars. The fixture does **not** enforce SQL foreign keys, unique indexes,
triggers, provider identity/rowversion behavior, locking or race guarantees. Invalid fixture references
can fail a subsequent saved-result read after saving; use typed scripts or the
[real-provider fixture](../testing/REPOSITORY-CONTRACT-FIXTURES.md) for provider guarantees.

## Receipts and boundaries

Configured link collections add `linkReconciliation` receipts with target type, both FK roles,
target principal keys, existing-reference-only semantics and the constraint/policy boundary. Existing
ordinary collection receipts and generated files remain unchanged. Fixture coverage reuses the
collectionSimulation receipt; its production link receipt identifies the more specific shape.

RGD3901 diagnoses unsupported FK roles or incomplete/reordered matching. Existing RGD2801/RGD2802
handle leaf/uniqueness/limit/initializer/update/version rules; RGD2903 still protects explicitly protected
payload/reference properties. RGD3402 handles missing fixture opt-ins/adapters in strict mode.

Implicit/shared property-bag joins, direct skip-navigation mutation, optional target FKs, unapproved/incompatible
overlapping FK columns, undeclared/optional/overlapping additional references, owned/inherited joins, nested/cascading graphs, inferred unlink/soft-delete
policies, native bulk linking and business workflows remain deliberate extensions/adapters. This
feature adds explicit association-row commands, not an automatic aggregate mapper.
