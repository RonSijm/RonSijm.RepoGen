# In-memory repository capability map

This document describes generic requirements for generated repository test doubles. The IM identifiers
are stable references used by the [feature plan](../future/REPOSITORY-FEATURE-PLAN.md) and
[implementation history](IMPLEMENTATION-HISTORY.md). For supported APIs, use the
[testing guides](../implemented/testing/README.md).

## 1. Fixture design

Generate test implementations of the same selected application ports used in production. Keep fixture
arrangement and inspection separate from those production contracts. Choose between scripted behavior
and explicitly supported execution of designer-defined queries and commands.

Use public book, catalog, notebook and inventory models in examples. Applications supply their own
mappings, seed values and business policies.

## 2. Generic feature map

| ID | Capability | Requirement |
| --- | --- | --- |
| IM01 | Exact application ports | Preserve selected read/write ports, overloads, value types, nullability and outcomes. Keep arrange/assert helpers on test-only contracts. |
| IM02 | Shared state and isolation | Share stores deliberately within a fixture or context key. Register aliases to the same intended instance; require explicit cross-scope sharing. |
| IM03 | Seed, reset and inspect | Provide typed seed/range/replace/reset/baseline/snapshot operations with explicit duplicate and reset policies. Return detached inspection data. |
| IM04 | Keys and generated values | Use declared keys, adapters and seed-aware value factories. Keep store identity separate from relationship and query comparison policies. |
| IM05 | Copy boundaries | Detach seed inputs, results and snapshots using a declared copy policy. Require adapters for unsupported graphs. |
| IM06 | Criteria and visibility | Reuse designer requests, predicates, fragments, guards and null/empty policies. Preserve required visibility; do not infer filters from request names. |
| IM07 | Related data and projections | Use declared source stores, relationship keys and portable joins/navigation. Validate unsupported shapes instead of dropping stages. |
| IM08 | Approved writes | Execute explicit create/patch/delete mappings and bounded collection policies. Preserve missing/duplicate/conflict outcomes and configured result shapes. |
| IM09 | Stage, save and rollback | Distinguish pending and committed state across participating stores. Make shared saves and checkpoint rollback deliberate. |
| IM10 | Conditional and immediate writes | Simulate only permitted predicates, expected-version outcomes and immediate commands. Database locking and constraint guarantees require provider tests. |
| IM11 | Scripted results and failures | Offer typed argument matchers, response/exception queues and asynchronous handlers. Unexpected calls fail clearly. |
| IM12 | Invocation recording | Record typed operation identities, frozen arguments and completion outcomes with bounded history and explicit reset. |
| IM13 | Cancellation and value dependencies | Respect cancellation and explicitly configured time/ID factories. Keep execution deterministic without artificial delays. |
| IM14 | Context and scope selection | Use explicit context keys and partition lifetimes. Defaults, encryption and application initialization remain consumer policies. |
| IM15 | Inspectable coverage | Report Generated, RequiresOverride or Unsupported per operation. Unsupported operations need explicit handlers; never return silent success. |
| IM16 | Contract verification | Reuse portable contract assertions across fixtures and real implementations, with separate provider-specific verification. |

### Criteria support: reuse the definition, not a second query language

A service calls the same named operation and request type in production and tests. Optional filters,
list membership, guarded nullable values, sorting, projections and terminals come from the designer.
Existing handwritten criteria helpers require explicit configuration or a typed override.

Supported operators and permissions are documented in [fixture queries](../implemented/testing/INMEMORY-QUERIES.md),
[model filters](../implemented/testing/INMEMORY-MODEL-FILTERS.md) and
[navigation reads](../implemented/testing/INMEMORY-NAVIGATION.md). A valid production expression does
not automatically establish fixture support or equivalent provider behavior.

## 3. Supporting test infrastructure

Use typed shared stores, explicit fixture-key adapters, detached snapshots and deliberate DI aliases.
Public data shapes can represent internal persistence models through declared scalar and relationship
maps. Arrange fixtures independently of the production implementation; constructing a production
repository should not be necessary to seed an EF-free service test.

[MappedCollectionSetters](../../samples/ConfigurationExamples/MappedCollectionSetters/README.md)
demonstrates explicit named parent/child assignments, independently scoped grants and complete
participant maps under bounded owner-scoped reconciliation. Original tokens, failure restoration,
Keep/Delete and Stage/shared Save do not infer creation, identity, graph or provider state.

See [shared fixtures](../implemented/testing/INMEMORY-FIXTURES.md),
[public navigation maps](../implemented/testing/INMEMORY-PROXY-NAVIGATION.md) and
[contract/provider fixtures](../implemented/testing/REPOSITORY-CONTRACT-FIXTURES.md).

## 4. Per-operation capability receipts

A receipt identifies which designer pipeline can execute automatically and which operations need an
override. It records the selected backend and reasons for unsupported stages. Scripted responses can
cover service scenarios, but their presence does not establish execution of the production pipeline.
See [coverage receipts and scripted overrides](../implemented/testing/SCRIPTED-TEST-DOUBLES.md).

## 5. Explicit boundaries

Business validation, default lookup data, numbering rules, settings engines, cryptography and external
transport behavior remain application-owned. Copying a repository-shaped service into a fixture would
also copy responsibilities outside generic persistence generation.

CLR execution does not prove SQL collation, null behavior, translation, generated values, constraints,
transaction isolation or race correctness. Use real-provider tests for those properties.

## 6. F15: optional generated test doubles

Testing output is opt-in and keeps its runtime dependencies separate from production contracts.
The delivered slices have distinct responsibilities:

| Slice | Responsibility | Guide |
| --- | --- | --- |
| F15.1 | Exact ports, scripts and coverage | [Scripted doubles](../implemented/testing/SCRIPTED-TEST-DOUBLES.md) |
| F15.2 | Shared keyed stores, copying and baseline/reset | [Fixtures](../implemented/testing/INMEMORY-FIXTURES.md) |
| F15.3 | Portable designer query execution with explicit permissions | [Queries](../implemented/testing/INMEMORY-QUERIES.md) |
| F15.4 | Approved writes, versions and shared Stage/Save | [Writes](../implemented/testing/INMEMORY-WRITES.md) |
| F15.5 | Typed queued handlers, cancellation and bounded recording | [Scripted doubles](../implemented/testing/SCRIPTED-TEST-DOUBLES.md) |
| F15.6 | Shared contract suites and real-provider setup | [Contract fixtures](../implemented/testing/REPOSITORY-CONTRACT-FIXTURES.md) |

### Focused examples

The [public example index](../../samples/ConfigurationExamples/README.md) links each designer to its
actual generated artifacts and executable consumer. Start with
[ScriptedPorts](../../samples/ConfigurationExamples/ScriptedPorts/README.md),
[MemoryFixtures](../../samples/ConfigurationExamples/MemoryFixtures/README.md),
[MemoryCriteria](../../samples/ConfigurationExamples/MemoryCriteria/README.md) and
[MemorySharedWrites](../../samples/ConfigurationExamples/MemorySharedWrites/README.md).
[RelationalContractFixture](../../samples/ConfigurationExamples/RelationalContractFixture/README.md)
shares assertions with a generated SQLite implementation.

### Acceptance criteria

- No testing output or extra runtime dependency appears without opt-in configuration.
- The service uses the same selected application ports for real and test implementations.
- Fixtures isolate state, detach mutable inputs and expose explicit seed/reset/inspection behavior.
- Automatic execution preserves designed predicates, permissions, persistence and result policies.
- Coverage receipts and unexpected calls identify unsupported behavior clearly.
- Portable contract suites verify shared behavior; provider tests verify database guarantees.
