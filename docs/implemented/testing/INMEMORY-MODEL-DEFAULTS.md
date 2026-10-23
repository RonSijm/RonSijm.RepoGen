# Optional model constants and insert sentinels

`UseInMemoryModelDefaults()` opts one declared fixture store into **constant insert defaults**
from the final EF model. Off by default, requiring automatic commands and a writable entity-shaped
or complete public read/write-mapped store. It changes no production command, contract, API,
permission or provider setting.

```csharp
entity.HasInMemoryStore<SettingSeed, int>(row => row.Id, row => new(row.Id, row.Credits))
    .MapInMemoryEntity<SettingSeed>(row => new StoredSetting { Id = row.Id, Credits = row.Credits })
    .MapInMemoryWrites<SettingSeed>((row, saved) => new(row.Id, row.Credits))
    .UseInMemoryModelDefaults();
```

With model HasDefaultValue(5).HasSentinel(-1), a creation candidate with Credits -1 receives 5;
supplied zero remains zero. Boolean and enum sentinels work similarly. See
[EF8's sentinel semantics](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-8.0/whatsnew#sentinel-values-and-database-defaults).
Scalar creation/child maps must explicitly assign custom-sentinel members; persistence field
initializers and constructors are not inferred, even when a property factory exists.

Only ordinary insert-only writable scalar properties with actual constant defaults qualify.
Primitive numbers, bool, char/string, public enums (including unnamed values), decimal, Guid,
DateTime/Offset, DateOnly/TimeOnly and TimeSpan have bounded literal rendering. Typed comparisons
preserve small-number/nullable/enum boxing; strings/control characters are escaped. Arbitrary
objects are never formatted or converted through application methods.

SQL expressions, computed/update generation, keys, FKs, concurrency tokens, mutable binary/object
values and configured provider conversion/comparison are not automatically adapted.
ValueGeneratedNever means a database constraint does not become a fixture factory. Unsupported
generated columns retain RGD3402 coverage. Complete maps/round trips retain RGD3601.

Explicit HasInMemoryGeneratedValue factories win over model constants, running first in declaration
order. Remaining model constants run by ordinal property name. Dependencies between generated
fields must be configured explicitly; an earlier factory cannot rely on a later implicit constant.
Removing an explicit factory can expose its model constant again while the option stays enabled.
False disables model constants; replacing the store or removing its write mapping clears the option.

Explicit factories also accept portable **custom scalar sentinels**, without requiring this opt-in.
Default CLR sentinels retain their old output. Binary/object sentinels, configured conversions/
comparers and throw-on-explicit-insert generation retain RGD3501. Ignored explicit insert values
remain unconditional computed-factory behavior, not sentinel tests.

| Action | Behavior |
| --- | --- |
| Create / Stage / bounded CreateMany | Defaults during detached preparation, before staging |
| Value unequal to the sentinel | Preserved, including zero/false/null when not the sentinel |
| Patch / staged or immediate update | Insert defaults do not run |
| Seed / replace / baseline restore | Arranged values preserved; no insert defaults |
| Shared Save | Existing prepared values share Save with unrelated pending writes |
| Transaction rollback / DiscardChanges | Existing explicit checkpoint/reservation semantics |

No SQL, sequence, trigger, clock, constraint, collation or race is emulated. CLR constants do not
guarantee provider normalization or universal relational equivalence. Immediate inserts and
association children retain their separate eligibility/simulation rules; a store option does not
authorize native/graph operations. Manifests record origin, typed sentinels, precedence/order and
no provider equivalence. Unconfigured outputs remain unchanged.

See [explicit factories](INMEMORY-GENERATED-VALUES.md), [mapped writes](INMEMORY-PROXY-WRITES.md),
and [MemoryModelDefaults](../../../samples/ConfigurationExamples/MemoryModelDefaults/README.md).
Its [designer](../../../samples/ConfigurationExamples/MemoryModelDefaults/Configuration/DefaultSettingsRepositoryDesigner.cs)
maps to exact output, demonstrating sentinels, supplied values, seeds, Stage/shared Save, ordered
batches, update isolation and rollback/baselines without EF/models. Tests compile every layout/store
shape and literal type, check guards and compare generated SQLite operations with fixtures.
