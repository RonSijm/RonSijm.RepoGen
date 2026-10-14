# Explicit fixture generated-value adapters

`HasInMemoryGeneratedValue(property, factory)` extends optional [tracked fixture writes](INMEMORY-WRITES.md)
with deterministic, test-only scalar generation. It changes neither the production API/implementation
nor its cache fingerprints. Ordinary users receive no extra infrastructure. A generated property with
no required adapter remains uncovered; strict mode does not silently invent a value.

[CreateMany](../writes/CREATE-MANY-COMMANDS.md) also includes earlier candidates from that batch in the
factory's reservation snapshot without staging them early. Failed preparation consumes no row-derived
ID reservations; database identity sequences and external counters are not simulated.

See [GeneratedNotebookRepositoryDesigner](../../../samples/ConfigurationExamples/MemoryGeneratedValues/Configuration/GeneratedNotebookRepositoryDesigner.cs)
and its [exact declaration-to-output example](../../../samples/ConfigurationExamples/MemoryGeneratedValues/README.md).

## Configure explicitly, in order

```csharp
entity.HasInMemoryStore(row => row.Id, row => new GeneratedNote
    { Id = row.Id, Text = row.Text, TextLength = row.TextLength, Origin = row.Origin });
entity.HasInMemoryGeneratedValue(row => row.Id,
    (row, existing) => checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
entity.HasInMemoryGeneratedValue(row => row.Origin, (row, existing) => "designer");
entity.HasInMemoryGeneratedValue(row => row.TextLength, (row, existing) => row.Text.Length);
```

Requires selected entity-shaped or [explicitly write-mapped public stores](INMEMORY-PROXY-WRITES.md)
and `UseInMemoryTestCommands()`. The EF model must declare
insert/update generation for each mapped property. Model generation/save metadata determines when
its **explicit** fixture factory runs; SQL expressions and triggers are never evaluated. Independent
[UseInMemoryModelDefaults](INMEMORY-MODEL-DEFAULTS.md) can reuse eligible constant insert defaults;
it is off by default and does not infer identity/computed/provider behavior.
Factories run in declaration order, so later factories can read earlier generated candidate values.

`row` is a detached candidate; `existing` is a read-only collection of detached same-store rows.
It is not a queryable, context, key allocator service or cross-table view. Collection order is not a
contract: use an explicit selection/aggregation such as Max, not an incidental last row.
Only the declared property is assigned by generated code; the production command still controls
approved application assignments. In particular, explicitly accepting a generated-on-insert input
in a production mapping still requires `AllowWrite` where the ordinary write rules require it.

## Insert allocation versus update computation

| Model behavior | Fixture timing and inputs |
| --- | --- |
| Generated on insert, explicit input allowed | Before Stage, when the property equals its portable model sentinel; other supplied values survive, including zero/false when not the sentinel |
| Generated on insert, model ignores explicit input | Before Stage, unconditionally (for example a computed column) |
| Generated on update | During actual shared Save preparation, after approved columns merge; not at Stage and not for unchanged/unforced rows |
| Explicit immediate scalar Update simulation | After original-row setters are prepared, for every matching saved row; never flushes unrelated staged values |
| Explicit InsertIfAbsent/InsertOrUpdate simulation | Insert branch: saved rows only, no pending reservations; update branch: after original-row setters, preserving saved key/conflict target |
| Delete | Never generates values; it can compare already-known generated tokens without any adapter |

Insert allocation includes saved and **all reserved pending** rows in the selected context partition,
including pending deletions. It excludes the new candidate. This prevents two staged default-key
creates from choosing the same next key with a suitable factory. Keys are allocated before Stage,
not as EF temporary keys and not as database identities. Duplicate keys or generation/adapter failure
reject the operation without publishing a partial reservation.

Update factories see merged saved state and detached same-store save candidates. Earlier prepared
rows in that store are included; separate stores/partitions cannot share an implicit sequence.
All stores finish preparation before any commit, so failure, stale tokens or pre-commit cancellation
cannot partly persist generated values. Multiple staged patches on one row generate once per actual
save, not once per patch. Original expected tokens remain original until the save succeeds.

Saved scalar and [batch](INMEMORY-BATCHES.md) DTOs contain computed values, retaining batch input
order. Mutable values require complete deep-copy adapters. Result mapping still occurs after commit:
`RepositorySavedResultException` is not an unsaved-write outcome and must not trigger a blind retry.
Deletes return pre-deletion state and never invoke update factories.

With [SimulateImmediateWrites](INMEMORY-IMMEDIATE-WRITES.md), eligible immediate updates also invoke
explicit factories before commit. Ordinary setters for all targets are prepared first; callbacks see
detached same-store candidates and earlier computed values, not pending tracked assignments. Every
matching row counts, including unchanged assignments. Returned-value mapping finishes before this
immediate commit, unlike the tracked-save result boundary above. No provider evaluation order is promised.
Suitable [insert/upsert simulations](INMEMORY-ATOMIC-WRITES.md) invoke insert factories only when no saved
conflict target exists, using saved rows without pending reservations. Update-only factories cannot
supply an omitted insert column. These callbacks do not emulate SQL sequence allocation on conflicts;
a later tracked Save can collide with an immediately inserted key without losing its pending entries.

## Reset, rollback and ownership

Factories must be pure, deterministic, non-reentrant and free of external side effects. The runtime
passes detached state and rejects context re-entry, but cannot prove arbitrary entity getter/setter
purity. Generated expressions reject captured state, ambient clocks/randomness, unsupported methods
and navigation access. Use typed scripts/handlers for deliberately external test behavior.

For row-derived factories, rollback and baseline restore also restore the inputs that determine the
next value. `DiscardChanges` releases reservations. `ResetTestDouble` resets scripts/history only;
it does not discard pending/saved rows. External counters/clocks are **not** checkpointed/reset.
For example Max + 1 can reuse values after deleting the greatest saved key; it is not a promise of
monotonic SQL IDENTITY/sequence behavior, trigger parity, provider Unicode semantics or race safety.

`WithoutInMemoryGeneratedValue(property)` removes one adapter. Duplicate declarations fail clearly.
Replacing/removing the store clears all its adapters. Disabling automatic writes emits no factories,
and existing unselected generated output remains unchanged. Runtime registrations must occur once,
before any staging/transaction; the generated data constructor performs that registration automatically.

## Diagnostics and conservative boundaries

RGD3501 rejects invalid selected adapters: no matching entity-shaped/explicit write-mapped store, non-generated/unmapped
properties, non-public or init-only entity-shaped setters, update-generated keys, unsupported expressions,
nonportable binary/object or custom-comparison insert sentinels and throw-on-explicit-insert generation.
Portable custom scalar sentinels are supported without the model-default opt-in. The ordinary scalar/write prerequisites
still apply; declaring a factory does not adapt unmapped private/shadow/owned state, custom comparer/converter
behavior, EF filters/AutoInclude, graph/cascade writes, inferred proxy reverse mapping or arbitrary native operations.
Suitable immediate scalar Update/Delete require their own explicit simulation opt-in; a factory alone is not that opt-in.
Missing required factories produce the ordinary RGD3402 command coverage diagnostic in strict mode.
Partial non-strict command coverage still fails explicitly when invoked, unless a script overrides it.

Factories can explicitly simulate a token/default/computed scalar for a service test. They do not
establish actual database-generated rowversion behavior, atomic races, uniqueness constraints,
durability, caching or transaction isolation. Use [real-provider contract fixtures](REPOSITORY-CONTRACT-FIXTURES.md)
for those claims. Shared tests compare actual generated SQLite/fixture named operations for suitable
ID/default/computed/token examples, supplementing rather than replacing provider verification.

Manifest store receipts list adapter properties, expressions, insert conditions, create/update timing,
allocation/reset scope and `relationalEquivalence: false`. These are test-only capability receipts,
not production generation policies.
