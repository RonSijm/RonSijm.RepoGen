# Configuration examples

Start with an example's **Configuration**, not its `Program.cs`. The designer declares what
RepoGen generates; `Program.cs` only sets up SQLite/DI or test arrangements, uses repositories and checks the result.
All eighty-one examples keep their handwritten inputs together. They are self-contained and
do not require or modify an external target application.

## Suggested reading order

Each example has a **Designer → generated output** section mapping declarations to actual emitted APIs.
The output column below links directly to the generated API, not just its containing folder.
Pick a topic rather than reading all the advanced examples as required setup.

| Topic | Example | Designer | Generated API | What it adds |
| --- | --- | --- | --- | --- |
| 1. Basics | [Simple](Simple/README.md) | [SimpleRepositoryDesigner](Simple/Configuration/SimpleRepositoryDesigner.cs) | [INoteRepository](Simple/Generated/Repositories.g.cs) | One simple preset; inferred key/index lookups and ordinary CRUD |
| 1. Basics | [Configured](Configured/README.md) | [CatalogRepositoryDesigner](Configured/Configuration/CatalogRepositoryDesigner.cs) | [IProductRepository](Configured/Generated/Repositories.g.cs) + [named queries](Configured/Generated/Queries.g.cs) | Selected accessors, projections, sorting, paging, batch staging and caching |
| 2. Contract shapes | [Cqrs](Cqrs/README.md) | [WorkboardRepositoryDesigner](Cqrs/Configuration/WorkboardRepositoryDesigner.cs) | [Read/write selector ports](Cqrs/Generated/Repositories.g.cs) | Separate read/write selectors and per-entity capabilities |
| 2. Contract shapes | [Adapters](Adapters/README.md) | [TaskDeskRepositoryDesigner](Adapters/Configuration/TaskDeskRepositoryDesigner.cs) | [IReadDeskTaskRepository / IWriteDeskTaskRepository](Contracts/Generated/TaskDeskRepositories.g.cs) | EF-free ports, application IDs and reusable request/DTO mappings |
| 2. Contract shapes | [AccessorAdapters](AccessorAdapters/README.md) | [LookupDirectoryRepositoryDesigner](AccessorAdapters/Configuration/LookupDirectoryRepositoryDesigner.cs) | [IReadDirectoryEntryRepository](AccessorAdapters/Generated/Repositories.g.cs) + [named queries](AccessorAdapters/Generated/Queries.g.cs) | Explicit key/converter and composite-index adaptations |
| 2. Contract shapes | [MappedAccessors](MappedAccessors/README.md) | [AliasDirectoryRepositoryDesigner](MappedAccessors/Configuration/AliasDirectoryRepositoryDesigner.cs) | [Read selectors](MappedAccessors/Generated/Repositories.g.cs) + [configured DTOs](MappedAccessors/Generated/Queries.g.cs) | Explicit typed shadow-property lookups, inferred uniqueness and converter inputs |
| 3. Queries and loading | [Reports](Reports/README.md) | [ReportingRepositoryDesigner](Reports/Configuration/ReportingRepositoryDesigner.cs) | [IReadSaleRepository : ISalesReports](Contracts/Generated/ReportingRepositories.g.cs) | Grouped pages, typed request terminals, field sorting and guarded cursors |
| 3. Queries and loading | [Library](Library/README.md) | [LibraryRepositoryDesigner](Library/Configuration/LibraryRepositoryDesigner.cs) | [IReadBookRepository : IBookLibrary](Contracts/Generated/LibraryRepositories.g.cs) | Nested graph loading, named profiles and nullable EF-free results |
| 3. Queries and loading | [Membership](Membership/README.md) | [MembershipDirectoryRepositoryDesigner](Membership/Configuration/MembershipDirectoryRepositoryDesigner.cs) | [IReadDirectoryItemRepository : IMembershipDirectory](Contracts/Generated/MembershipRepositories.g.cs) | List inputs, reusable predicates, input limits and dictionary policies |
| 3. Queries and loading | [Visibility](Visibility/README.md) | [VisibilityRepositoryDesigner](Visibility/Configuration/VisibilityRepositoryDesigner.cs) | [IReadShowcaseDocumentRepository : IReviewedLibrary](Contracts/Generated/VisibilityRepositories.g.cs) | Navigation policy review, global filters and trusted read operations |
| 4. Writes and infrastructure | [Inventory](Inventory/README.md) | [InventoryRepositoryDesigner](Inventory/Configuration/InventoryRepositoryDesigner.cs) | [IReadInventoryItemRepository / IWriteInventoryItemRepository](Contracts/Generated/InventoryRepositories.g.cs) | Native returning writes with a direct context |
| 4. Writes and infrastructure | [ShadowReturning](ShadowReturning/README.md) | [AuditDocumentsRepositoryDesigner](ShadowReturning/Configuration/AuditDocumentsRepositoryDesigner.cs) | [Audit document ports](Contracts/Generated/AuditDocumentsRepositories.g.cs) + [native operations](ShadowReturning/Generated/Repositories.g.cs) | Per-command shadow audit/computed results, filters, versions and no tracking or implicit Save |
| 4. Writes and infrastructure | [ShadowVersions](ShadowVersions/README.md) | [VersionedNotesRepositoryDesigner](ShadowVersions/Configuration/VersionedNotesRepositoryDesigner.cs) | [EF-free ports](Contracts/Generated/VersionedNotesRepositories.g.cs) + [write operations](ShadowVersions/Generated/Repositories.g.cs) | Design-time shadow concurrency checks in tracked, native and bounded batch writes |
| 4. Writes and infrastructure | [MappedReturning](MappedReturning/README.md) | [CapturedNotesRepositoryDesigner](MappedReturning/Configuration/CapturedNotesRepositoryDesigner.cs) | [Write ports](Contracts/Generated/CapturedNotesRepositories.g.cs) + [native operations](MappedReturning/Generated/Repositories.g.cs) | Explicit native field-backed/indexer/shadow results, getter-free DTO values and field-only version checks |
| 4. Writes and infrastructure | [MappedSetters](MappedSetters/README.md) | [EditableNotesRepositoryDesigner](MappedSetters/Configuration/EditableNotesRepositoryDesigner.cs) | [Mapped scalar setters](MappedSetters/Persistence/Repositories.g.cs) + [independent fixture](MappedSetters/Generated/TestDoubles.g.cs) | Exact field/shadow/indexer targets, conditional tracked writes, original versions and Stage/shared Save through EF-free ports |
| 4. Writes and infrastructure | [MappedBatchSetters](MappedBatchSetters/README.md) | [ReviewEntriesRepositoryDesigner](MappedBatchSetters/Configuration/ReviewEntriesRepositoryDesigner.cs) | [Mapped batch setters](MappedBatchSetters/Persistence/Repositories.g.cs) + [independent fixture](MappedBatchSetters/Generated/TestDoubles.g.cs) | Bounded field/shadow/indexer batches, stale-item restoration, original tokens and input-ordered saved results through EF-free ports |
| 4. Writes and infrastructure | [MappedCollectionSetters](MappedCollectionSetters/README.md) | [ReviewBoardsRepositoryDesigner](MappedCollectionSetters/Configuration/ReviewBoardsRepositoryDesigner.cs) | [Mapped child updates](MappedCollectionSetters/Persistence/Repositories.g.cs) + [independent fixture](MappedCollectionSetters/Generated/TestDoubles.g.cs) | Separate parent/child permissions, owner-scoped mapped fields, original tokens, Keep/Delete and Stage/shared Save through EF-free ports |
| 4. Writes and infrastructure | [Collections](Collections/README.md) | [ChecklistRepositoryDesigner](Collections/Configuration/ChecklistRepositoryDesigner.cs) | [IWriteChecklistRepository](Contracts/Generated/CollectionsRepositories.g.cs) | Explicit owner-scoped child add/update/delete versus safe merge defaults |
| 4. Writes and infrastructure | [WriteSafety](WriteSafety/README.md) | [AuditedNotesRepositoryDesigner](WriteSafety/Configuration/AuditedNotesRepositoryDesigner.cs) | [IWriteAuditedNoteRepository](Contracts/Generated/WriteSafetyRepositories.g.cs) | Ordinary audit/ownership scalar protection and deliberate command-local permissions |
| 4. Writes and infrastructure | [BatchWrites](BatchWrites/README.md) | [BatchNotesRepositoryDesigner](BatchWrites/Configuration/BatchNotesRepositoryDesigner.cs) | [IReadBatchNoteRepository / IWriteBatchNoteRepository](Contracts/Generated/BatchWritesRepositories.g.cs) | Bounded named patches/deletes, per-item versions, partial inputs and one shared save |
| 4. Writes and infrastructure | [Ledger](Ledger/README.md) | [LedgerRepositoryDesigner](Ledger/Configuration/LedgerRepositoryDesigner.cs) | [IReadLedgerEntryRepository](Contracts/Generated/Ledger/RonSijm.RepoGen.ConfigurationExamples.Contracts.Ledger.Generated.IReadLedgerEntryRepository.g.cs) / [IWriteLedgerEntryRepository](Contracts/Generated/Ledger/RonSijm.RepoGen.ConfigurationExamples.Contracts.Ledger.Generated.IWriteLedgerEntryRepository.g.cs) | Optional keyed leases, unit of work, version checks, atomic writes and scoped caching |
| 4. Writes and infrastructure | [MultiContext](MultiContext/README.md) | [Primary](MultiContext/Configuration/PrimaryNoteRepositoryDesigner.cs) / [Archive](MultiContext/Configuration/ArchiveNoteRepositoryDesigner.cs) | [Primary selector ports](MultiContext/Generated/RonSijm.RepoGen.ConfigurationExamples.MultiContext.Primary.StoredNote.Repository.g.cs) / [archive read port](Contracts/Generated/MultiContext/RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ArchiveNotes.IReadStoredNoteRepository.g.cs) | Independent context/repository namespaces sharing an entity in one generation batch |
| 5. Testing | [ScriptedPorts](ScriptedPorts/README.md) | [ScriptedNotesRepositoryDesigner](ScriptedPorts/Configuration/ScriptedNotesRepositoryDesigner.cs) | [ScriptedNoteRepositoryTestDouble](ScriptedPorts/Generated/TestDoubles.g.cs) + [closed ports](Contracts/Generated/ScriptedNotesRepositories.g.cs) | EF-free bound ports, overload scripts, queued write outcomes and snapshotted recording |
| 5. Testing | [ScriptedStreams](ScriptedStreams/README.md) | [ScriptedFeedRepositoryDesigner](ScriptedStreams/Configuration/ScriptedFeedRepositoryDesigner.cs) | [FeedEntryRepositoryTestDouble](ScriptedStreams/Generated/TestDoubles.g.cs) + [read port](Contracts/Generated/ScriptedFeedRepositories.g.cs) | One cold stream with early-disposal/cancellation outcomes; no writes or EF consumer dependency |
| 5. Testing | [MemoryFixtures](MemoryFixtures/README.md) | [MemoryNotebookRepositoryDesigner](MemoryFixtures/Configuration/MemoryNotebookRepositoryDesigner.cs) | [Shared fixture/stores](MemoryFixtures/Generated/TestDoubles.g.cs) + [read ports](Contracts/Generated/MemoryNotebookRepositories.g.cs) | Two scoped stores, typed keys, deep-copy isolation and atomic baseline reset; operations remain scripted |
| 5. Testing | [MemoryPartitions](MemoryPartitions/README.md) | [PartitionDirectoryRepositoryDesigner](MemoryPartitions/Configuration/PartitionDirectoryRepositoryDesigner.cs) | [Keyed fixture/public storage proxy](MemoryPartitions/Generated/TestDoubles.g.cs) + [read port](Contracts/Generated/MemoryPartitionsRepositories.g.cs) | Typed partitions, public data shape, per-key reset and explicit singleton data across scoped scripts |
| 5. Testing | [MemoryCriteria](MemoryCriteria/README.md) | [MemoryCatalogRepositoryDesigner](MemoryCriteria/Configuration/MemoryCatalogRepositoryDesigner.cs) | [Automatic reads/keyed fixture](MemoryCriteria/Generated/TestDoubles.g.cs) + [application read port](Contracts/Generated/MemoryCatalogRepositories.g.cs) | Same designer criteria, visibility, named projections, sorting/pages, grouped totals and streams without EF |
| 5. Testing | [MemoryCoverage](MemoryCoverage/README.md) | [MemoryCoverageRepositoryDesigner](MemoryCoverage/Configuration/MemoryCoverageRepositoryDesigner.cs) | [Covered reads/typed overrides](MemoryCoverage/Generated/TestDoubles.g.cs) + [closed ports](Contracts/Generated/MemoryCoverageRepositories.g.cs) | Strict read coverage, explicit EF-filter/navigation acknowledgements and unsupported-write scripts |
| 5. Testing | [MemoryModelFilters](MemoryModelFilters/README.md) | [FilteredNotebookRepositoryDesigner](MemoryModelFilters/Configuration/FilteredNotebookRepositoryDesigner.cs) | [Filtered roots](MemoryModelFilters/Generated/TestDoubles.g.cs) + [bound read port](Contracts/Generated/FilteredNotebookRepositories.g.cs) | Optional final-model row filters, independent visibility/bypasses, filtered From joins, counted pages and cold streams without EF/models |
| 5. Testing | [MemoryFilteredNavigation](MemoryFilteredNavigation/README.md) | [FilteredShelfRepositoryDesigner](MemoryFilteredNavigation/Configuration/FilteredShelfRepositoryDesigner.cs) | [Filtered graphs](MemoryFilteredNavigation/Generated/TestDoubles.g.cs) + [bound readers](Contracts/Generated/FilteredShelfRepositories.g.cs) | Separate graph filter opt-in, collection counts/DTOs, guarded optional references, independent visibility, From/pages/streams and detached snapshots |
| 5. Testing | [MemoryModelWrites](MemoryModelWrites/README.md) | [FilteredDraftsRepositoryDesigner](MemoryModelWrites/Configuration/FilteredDraftsRepositoryDesigner.cs) | [Filtered commands](MemoryModelWrites/Generated/TestDoubles.g.cs) + [bound CQRS/audit ports](Contracts/Generated/FilteredDraftsRepositories.g.cs) | Per-command model filters, version outcomes, saved-row staging, cross-repository Save, immediate counts, unfiltered creation and explicit saved-result rollback |
| 5. Testing | [MemoryLoadingPolicies](MemoryLoadingPolicies/README.md) | [ExplicitLoadingRepositoryDesigner](MemoryLoadingPolicies/Configuration/ExplicitLoadingRepositoryDesigner.cs) | [Explicit read fixtures](MemoryLoadingPolicies/Generated/TestDoubles.g.cs) + [bound reader](Contracts/Generated/AutoLoadedCatalogRepositories.g.cs) | Per-query model-loading suppression, retained profiles, mapped DTOs, From roots, pages, streams and detached baseline isolation |
| 5. Testing | [MemoryJoins](MemoryJoins/README.md) | [OrderReportsRepositoryDesigner](MemoryJoins/Configuration/OrderReportsRepositoryDesigner.cs) | [Multi-store reads](MemoryJoins/Generated/TestDoubles.g.cs) + [application report port](Contracts/Generated/OrderReportsRepositories.g.cs) | Composite/left joins, correlated anti-joins, unions and grouped pages over coordinated fixture snapshots |
| 5. Testing | [MemoryProxies](MemoryProxies/README.md) | [ProxyDirectoryRepositoryDesigner](MemoryProxies/Configuration/ProxyDirectoryRepositoryDesigner.cs) | [Mapped read rows/fixture](MemoryProxies/Generated/TestDoubles.g.cs) + [application read port](Contracts/Generated/ProxyDirectoryRepositories.g.cs) | Explicit public scalar mappings for internal EF models, with unmapped-field coverage and no model dependency |
| 5. Testing | [MemoryNavigation](MemoryNavigation/README.md) | [NavigationCatalogRepositoryDesigner](MemoryNavigation/Configuration/NavigationCatalogRepositoryDesigner.cs) | [Declared navigation loading](MemoryNavigation/Generated/TestDoubles.g.cs) + [application read port](Contracts/Generated/NavigationCatalogRepositories.g.cs) | Mapped-key reference/collection loading, nested DTOs, unfiltered profiles and explicit related visibility over one fixture snapshot |
| 5. Testing | [MemoryProxyNavigation](MemoryProxyNavigation/README.md) | [ProxyShelfRepositoryDesigner](MemoryProxyNavigation/Configuration/ProxyShelfRepositoryDesigner.cs) | [Mapped related read rows](MemoryProxyNavigation/Generated/TestDoubles.g.cs) + [application read port](Contracts/Generated/ProxyShelfRepositories.g.cs) | Internal/read-only EF models, immutable public seeds, composite navigation keys and nested DTOs with no model dependency |
| 5. Testing | [MemoryManyToMany](MemoryManyToMany/README.md) | [TaggedArticleRepositoryDesigner](MemoryManyToMany/Configuration/TaggedArticleRepositoryDesigner.cs) | [Typed join loading](MemoryManyToMany/Generated/TestDoubles.g.cs) + [closed contracts](Contracts/Generated/TaggedArticleRepositories.g.cs) | Explicit mapped join stores, composite principal keys, accessed inverses, nested DTOs and related visibility without EF/model consumer dependencies |
| 5. Testing | [MemoryOptionalLinks](MemoryOptionalLinks/README.md) | [LinkedNotesRepositoryDesigner](MemoryOptionalLinks/Configuration/LinkedNotesRepositoryDesigner.cs) | [Guarded nullable-link loading](MemoryOptionalLinks/Generated/TestDoubles.g.cs) + [read contracts](Contracts/Generated/LinkedNoteRepositories.g.cs) | Optional source links, explicit non-null target filters, inverse counts and visible provider-bound overrides without EF/model consumer dependencies |
| 5. Testing | [MemoryNullTargets](MemoryNullTargets/README.md) | [NullTargetNotebookRepositoryDesigner](MemoryNullTargets/Configuration/NullTargetNotebookRepositoryDesigner.cs) | [Null-preserving reports](MemoryNullTargets/Generated/TestDoubles.g.cs) + [read contracts](Contracts/Generated/NullTargetNotebookRepositories.g.cs) | Per-navigation opt-in, repeated missing-target slots, explicit null-safe counts/presence/DTOs, reused list/page/stream projection and detached snapshots |
| 5. Testing | [MemoryRelationshipComparers](MemoryRelationshipComparers/README.md) | [ComparedDirectoryRepositoryDesigner](MemoryRelationshipComparers/Configuration/ComparedDirectoryRepositoryDesigner.cs) | [Compared relationships](MemoryRelationshipComparers/Generated/TestDoubles.g.cs) + [read contracts](Contracts/Generated/ComparedDirectoryRepositories.g.cs) | One principal-key component policy shared by both directions; composite scope, uniqueness, null FKs and unchanged query/store identity |
| 5. Testing | [MemoryLinks](MemoryLinks/README.md) | [BoardLabelsRepositoryDesigner](MemoryLinks/Configuration/BoardLabelsRepositoryDesigner.cs) | [Join-row writes](MemoryLinks/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/BoardLabelsRepositories.g.cs) | Explicit linking, payload updates and unlinking through join rows, parent/link versions, shared Stage/Save, saved DTOs and rollback without endpoint graph mutation |
| 5. Testing | [MemorySharedLinks](MemorySharedLinks/README.md) | [ScopedResourcesRepositoryDesigner](MemorySharedLinks/Configuration/ScopedResourcesRepositoryDesigner.cs) | [Owner-checked join writes](MemorySharedLinks/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/ScopedResourcesRepositories.g.cs) | Explicit overlapping FK columns, complete reference keys, cross-owner rejection, public mapped DTOs and unchanged Stage/Save/rollback |
| 5. Testing | [MemorySharedReferences](MemorySharedReferences/README.md) | [SharedAuditRepositoryDesigner](MemorySharedReferences/Configuration/SharedAuditRepositoryDesigner.cs) | [Add-only shared-reference guards](MemorySharedReferences/Generated/TestDoubles.g.cs) + [bound ports](Contracts/Generated/SharedAuditRepositories.g.cs) | Additional FK columns shared with ownership outside the target key, immutable matched references, fresh DTOs and Stage/Save/rollback |
| 5. Testing | [MemoryScopedOptional](MemoryScopedOptional/README.md) | [ScopedReviewRepositoryDesigner](MemoryScopedOptional/Configuration/ScopedReviewRepositoryDesigner.cs) | [Independent nullable presence](MemoryScopedOptional/Generated/TestDoubles.g.cs) + [bound ports](Contracts/Generated/ScopedReviewRepositories.g.cs) | Optional references with required shared ownership, partial-null/cross-owner rejection, immutable matches, fresh versioned DTOs and Stage/Save/rollback |
| 5. Testing | [MemoryAdditionalReferences](MemoryAdditionalReferences/README.md) | [ReferenceCatalogRepositoryDesigner](MemoryAdditionalReferences/Configuration/ReferenceCatalogRepositoryDesigner.cs) | [Explicit reference roles](MemoryAdditionalReferences/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/ReferenceCatalogRepositories.g.cs) | Named matching target, complete composite/scalar Add-only references, immutable matched FKs, public mapped DTOs and shared Stage/Save/rollback |
| 5. Testing | [MemoryOptionalReferences](MemoryOptionalReferences/README.md) | [OptionalAssignmentsRepositoryDesigner](MemoryOptionalReferences/Configuration/OptionalAssignmentsRepositoryDesigner.cs) | [Nullable reference writes](MemoryOptionalReferences/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/OptionalAssignmentsRepositories.g.cs) | Explicit optional additional FK, all-null absence/all-present reference, partial-key rejection, immutable presence/values and shared Stage/Save/rollback |
| 5. Testing | [MemoryTranslations](MemoryTranslations/README.md) | [PatternCatalogRepositoryDesigner](MemoryTranslations/Configuration/PatternCatalogRepositoryDesigner.cs) | [Translated reads](MemoryTranslations/Generated/TestDoubles.g.cs) + [application contracts](Contracts/Generated/PatternCatalogRepositories.g.cs) | Exact provider/helper call translations, explicit ordinal/escaped patterns, scalar/group projections and streams without changing production SQL or exposing EF/models |
| 5. Testing | [MemoryProjectionHelpers](MemoryProjectionHelpers/README.md) | [ProjectionMemosRepositoryDesigner](MemoryProjectionHelpers/Configuration/ProjectionMemosRepositoryDesigner.cs) | [DTO expression fixtures](MemoryProjectionHelpers/Generated/TestDoubles.g.cs) + [read/write contracts](Contracts/Generated/ProjectionMemosRepositories.g.cs) | Scalar-input flat DTO helper recipes, original production mapping, named projection reuse and independently opted-in fresh saved results |
| 5. Testing | [MemoryNestedProjections](MemoryNestedProjections/README.md) | [BriefingsRepositoryDesigner](MemoryNestedProjections/Configuration/BriefingsRepositoryDesigner.cs) | [Nested result-data fixtures](MemoryNestedProjections/Generated/TestDoubles.g.cs) + [bound ports](Contracts/Generated/BriefingsRepositories.g.cs) | Per-recipe nested DTO/array/list construction, materialized mapper results, original production mapping and independent command consent |
| 5. Testing | [MemoryWriteTranslations](MemoryWriteTranslations/README.md) | [PatternNotesRepositoryDesigner](MemoryWriteTranslations/Configuration/PatternNotesRepositoryDesigner.cs) | [Translated commands](MemoryWriteTranslations/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/PatternNotesRepositories.g.cs) | Per-command scalar translations, mapped Stage/Save/versions, fresh SQL-function results and immediate deletion; production source/protections stay unchanged |
| 5. Testing | [MemoryScalarEquality](MemoryScalarEquality/README.md) | [ComparisonNotesRepositoryDesigner](MemoryScalarEquality/Configuration/ComparisonNotesRepositoryDesigner.cs) | [Tracked scalar policies](MemoryScalarEquality/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/ComparisonNotesRepositories.g.cs) | Explicit nullable state/version equality, equivalent staged/saved values, unchanged query/key semantics and strict public-map round trips |
| 5. Testing | [MemorySharedWrites](MemorySharedWrites/README.md) | [DraftWorkspaceRepositoryDesigner](MemorySharedWrites/Configuration/DraftWorkspaceRepositoryDesigner.cs) | [Stateful write doubles](MemorySharedWrites/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/DraftWorkspaceRepositories.g.cs) | Explicit Create/Patch/Delete, versions, pending/saved rows, cross-repository Stage/Save and checkpoint rollback |
| 5. Testing | [MemoryBatches](MemoryBatches/README.md) | [BatchWorkspaceRepositoryDesigner](MemoryBatches/Configuration/BatchWorkspaceRepositoryDesigner.cs) | [Bounded batch doubles](MemoryBatches/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/BatchWorkspaceRepositories.g.cs) | Alternate unique matching keys, all-target preparation, optional fields, ordered saved DTOs and Stage/Save/rollback |
| 5. Testing | [MemoryGeneratedValues](MemoryGeneratedValues/README.md) | [GeneratedNotebookRepositoryDesigner](MemoryGeneratedValues/Configuration/GeneratedNotebookRepositoryDesigner.cs) | [Explicit scalar factories](MemoryGeneratedValues/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/GeneratedNotebookRepositories.g.cs) | Seed/pending-aware IDs, supplied defaults, save-time computed tokens, generated batch results and rollback/baseline replay |
| 5. Testing | [MemoryModelDefaults](MemoryModelDefaults/README.md) | [DefaultSettingsRepositoryDesigner](MemoryModelDefaults/Configuration/DefaultSettingsRepositoryDesigner.cs) | [Constant/sentinel fixtures](MemoryModelDefaults/Generated/TestDoubles.g.cs) + [bound ports](Contracts/Generated/DefaultSettingsRepositories.g.cs) | Optional constant model defaults, typed unset/boolean/enum sentinels, supplied zero/false values, mapped Stage/Save, ordered batches and rollback/baselines |
| 5. Testing | [MemoryProxyWrites](MemoryProxyWrites/README.md) | [ProxyNotebookRepositoryDesigner](MemoryProxyWrites/Configuration/ProxyNotebookRepositoryDesigner.cs) | [Mapped write fixtures](MemoryProxyWrites/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/ProxyNotebookRepositories.g.cs) | Internal models, immutable public rows, explicit scalar write adapters, shared saves, generated values, batches and rollback |
| 5. Testing | [MemoryKeyIdentity](MemoryKeyIdentity/README.md) | [KeyDirectoryRepositoryDesigner](MemoryKeyIdentity/Configuration/KeyDirectoryRepositoryDesigner.cs) | [Comparer-backed fixtures](MemoryKeyIdentity/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/KeyDirectoryRepositories.g.cs) | Opt-in fixture key equality, unchanged predicates, duplicate policies, mapped Stage/Save and baseline/checkpoint restore |
| 5. Testing | [MemoryImmediateWrites](MemoryImmediateWrites/README.md) | [DispatchQueueRepositoryDesigner](MemoryImmediateWrites/Configuration/DispatchQueueRepositoryDesigner.cs) | [Immediate write fixtures](MemoryImmediateWrites/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/DispatchQueueRepositories.g.cs) | Per-command scalar Update/Delete/consume simulation, original-row setters, counts/outcomes, pending isolation and rollback |
| 5. Testing | [MemoryAtomicWrites](MemoryAtomicWrites/README.md) | [ReservationDirectoryRepositoryDesigner](MemoryAtomicWrites/Configuration/ReservationDirectoryRepositoryDesigner.cs) | [Insert/upsert fixtures](MemoryAtomicWrites/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/ReservationDirectoryRepositories.g.cs) | Per-command insert/existing/conditional-upsert outcomes, composite conflict targets, update-only visibility/versions, explicit IDs, retained staging and rollback |
| 5. Testing | [MemoryAtomicCapture](MemoryAtomicCapture/README.md) | [CapturedReservationsRepositoryDesigner](MemoryAtomicCapture/Configuration/CapturedReservationsRepositoryDesigner.cs) | [Captured insert/upsert fixtures](MemoryAtomicCapture/Generated/TestDoubles.g.cs) + [bound writer port](Contracts/Generated/CapturedReservationsRepositories.g.cs) | Explicit native field/shadow/indexer results, independent fixture state/factories, original versions, immutable identity and rollback without EF/models |
| 5. Testing | [MemoryAtomicResults](MemoryAtomicResults/README.md) | [EnrollmentRepositoryDesigner](MemoryAtomicResults/Configuration/EnrollmentRepositoryDesigner.cs) | [Fresh atomic result fixtures](MemoryAtomicResults/Generated/TestDoubles.g.cs) + [bound writer port](Contracts/Generated/EnrollmentRepositories.g.cs) | Separate pre-commit DTO graph queries, saved primary keys, prepared root overlays and saved-only related data without EF/models |
| 5. Testing | [MemoryNativeResults](MemoryNativeResults/README.md) | [QueueResultsRepositoryDesigner](MemoryNativeResults/Configuration/QueueResultsRepositoryDesigner.cs) | [Bounded native-update result fixtures](MemoryNativeResults/Generated/TestDoubles.g.cs) + [bound ports](Contracts/Generated/QueueResultsRepositories.g.cs) | Fresh multi-row DTO graphs, explicit abort-on-excess bounds, complete keys, original versions and pre-commit rollback without EF/models |
| 5. Testing | [MemoryCollections](MemoryCollections/README.md) | [ChecklistWorkspaceRepositoryDesigner](MemoryCollections/Configuration/ChecklistWorkspaceRepositoryDesigner.cs) | [Child-store reconciliation](MemoryCollections/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/ChecklistWorkspaceRepositories.g.cs) | Explicit owner-scoped child add/update/missing policies, public scalar maps, reserved IDs, parent/child versions, shared staging/save and rollback |
| 5. Testing | [CompositeCollections](CompositeCollections/README.md) | [TranslationCatalogRepositoryDesigner](CompositeCollections/Configuration/TranslationCatalogRepositoryDesigner.cs) | [Composite child matching](CompositeCollections/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/TranslationCatalogRepositories.g.cs) | Complete Code/Locale matching within composite ownership, immutable components, null/duplicate guards, public rows, versions and Stage/Save/rollback |
| 5. Testing | [MemorySavedResults](MemorySavedResults/README.md) | [SavedNotebookRepositoryDesigner](MemorySavedResults/Configuration/SavedNotebookRepositoryDesigner.cs) | [Saved result queries](MemorySavedResults/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/SavedNotebookRepositories.g.cs) | Fresh nested DTOs after shared save/reconciliation, created references, mapped public rows, explicit visibility and rollback |
| 5. Testing | [MemoryBatchResults](MemoryBatchResults/README.md) | [BatchNotebookRepositoryDesigner](MemoryBatchResults/Configuration/BatchNotebookRepositoryDesigner.cs) | [Saved batch queries](MemoryBatchResults/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/BatchNotebookRepositories.g.cs) | One fresh nested result read for a bounded patch batch, complete composite keys, input order, shared save and rollback |
| 5. Testing | [MemoryCreateBatches](MemoryCreateBatches/README.md) | [IntakeCatalogRepositoryDesigner](MemoryCreateBatches/Configuration/IntakeCatalogRepositoryDesigner.cs) | [Create fixtures](MemoryCreateBatches/Generated/TestDoubles.g.cs) + [CQRS contracts](Contracts/Generated/IntakeCatalogRepositories.g.cs) | Bounded scalar creation, reserved IDs, fresh ordered DTOs, shared staging/save, preparation failure and rollback |
| 5. Testing | [MemorySavedFilters](MemorySavedFilters/README.md) | [SavedShelfRepositoryDesigner](MemorySavedFilters/Configuration/SavedShelfRepositoryDesigner.cs) | [Filtered saved DTOs](MemorySavedFilters/Generated/TestDoubles.g.cs) + [bound readers/writers](Contracts/Generated/SavedShelfRepositories.g.cs) | Independent command/related graph filters, collection counts/optional targets, ordered batches, shared Save and explicit rollback after committed result failure |
| 5. Testing | [MemoryNativeFilters](MemoryNativeFilters/README.md) | [NativeNotebookRepositoryDesigner](MemoryNativeFilters/Configuration/NativeNotebookRepositoryDesigner.cs) | [Filtered native results](MemoryNativeFilters/Generated/TestDoubles.g.cs) + [bound ports](Contracts/Generated/NativeNotebookRepositories.g.cs) | Separate native/model-filter opt-ins, post-update/pre-delete values, no fresh result filtering, retained pending state, token conflicts and checkpoint rollback |
| 5. Testing | [MemoryContextFilters](MemoryContextFilters/README.md) | [TenantNotebookRepositoryDesigner](MemoryContextFilters/Configuration/TenantNotebookRepositoryDesigner.cs) | [Typed context-value filters](MemoryContextFilters/Generated/TestDoubles.g.cs) + [bound ports](Contracts/Generated/TenantNotebookRepositories.g.cs) | Explicit member/key recipes, filtered reads/writes/fresh DTOs, retained staged state and isolated partitions without a fixture DbContext |
| 5. Testing | [MemoryShadowReads](MemoryShadowReads/README.md) | [RankedNotebookRepositoryDesigner](MemoryShadowReads/Configuration/RankedNotebookRepositoryDesigner.cs) | [Typed shadow read rows](MemoryShadowReads/Generated/TestDoubles.g.cs) + [bound read port](Contracts/Generated/RankedNotebookRepositories.g.cs) | Explicit model-only scalars in filters, ordering, pages and grouped reports; no EF dependency or tracked shadow state |
| 5. Testing | [MemoryMappedReads](MemoryMappedReads/README.md) | [MappedNotebookRepositoryDesigner](MemoryMappedReads/Configuration/MappedNotebookRepositoryDesigner.cs) | [Typed field/indexer read rows](MemoryMappedReads/Generated/TestDoubles.g.cs) + [bound read port](Contracts/Generated/MappedNotebookRepositories.g.cs) | Unified exact property bindings, getter-free values, model filters and safe sorting without EF/models in the consumer |
| 5. Testing | [MemoryMappedKeys](MemoryMappedKeys/README.md) | [KeyedNotebookRepositoryDesigner](MemoryMappedKeys/Configuration/KeyedNotebookRepositoryDesigner.cs) | [Detached key-based graph reads](MemoryMappedKeys/Generated/TestDoubles.g.cs) + [bound read ports](Contracts/Generated/KeyedNotebookRepositories.g.cs) | Explicit shadow/field relationship keys, per-edge permission and separate fixture dictionary identity |
| 5. Testing | [MemoryMappedWrites](MemoryMappedWrites/README.md) | [RevisionNotebookRepositoryDesigner](MemoryMappedWrites/Configuration/RevisionNotebookRepositoryDesigner.cs) | [Typed scalar write state](MemoryMappedWrites/Generated/TestDoubles.g.cs) + [bound read/write ports](Contracts/Generated/RevisionNotebookRepositories.g.cs) | Separate shadow-version state permission, explicit factories, Stage/shared Save and fresh saved DTOs without EF/models |
| 5. Testing | [MemoryMappedReturning](MemoryMappedReturning/README.md) | [ReceiptQueueRepositoryDesigner](MemoryMappedReturning/Configuration/ReceiptQueueRepositoryDesigner.cs) | [Mapped mutation receipts](MemoryMappedReturning/Generated/TestDoubles.g.cs) + [bound ports](Contracts/Generated/ReceiptQueueRepositories.g.cs) | Explicit post-update/pre-delete field/shadow scalars, versions, detached receipts and checkpoint rollback without EF/models |
| 5. Testing | [MemoryMappedCollections](MemoryMappedCollections/README.md) | [RevisionChecklistRepositoryDesigner](MemoryMappedCollections/Configuration/RevisionChecklistRepositoryDesigner.cs) | [Mapped collection state](MemoryMappedCollections/Generated/TestDoubles.g.cs) + [bound ports](Contracts/Generated/VersionedChecklistRepositories.g.cs) | Independent parent/child shadow versions, bounded reconciliation, Stage/shared Save and checkpoints without EF/models |
| 5. Testing | [MemoryAtomicFilters](MemoryAtomicFilters/README.md) | [FilteredReservationRepositoryDesigner](MemoryAtomicFilters/Configuration/FilteredReservationRepositoryDesigner.cs) | [Filtered atomic branches](MemoryAtomicFilters/Generated/TestDoubles.g.cs) + [bound ports](Contracts/Generated/FilteredReservationRepositories.g.cs) | Unfiltered conflicts/inserts, filtered update selection/model-only result checks, retained keys/pending state, tokens and rollback |
| 5. Testing | [MemoryFilteredLinks](MemoryFilteredLinks/README.md) | [FilteredCatalogRepositoryDesigner](MemoryFilteredLinks/Configuration/FilteredCatalogRepositoryDesigner.cs) | [Filtered explicit joins](MemoryFilteredLinks/Generated/TestDoubles.g.cs) + [closed contracts](Contracts/Generated/FilteredCatalogRepositories.g.cs) | Model-filtered endpoints/join rows, surviving empty owners, inverse reads, paging and independently opted-in fresh saved DTOs |
| 5. Testing | [MemoryFilteredCollections](MemoryFilteredCollections/README.md) | [FilteredChecklistRepositoryDesigner](MemoryFilteredCollections/Configuration/FilteredChecklistRepositoryDesigner.cs) | [Filtered collection writes](MemoryFilteredCollections/Generated/TestDoubles.g.cs) + [closed contracts](Contracts/Generated/FilteredChecklistRepositories.g.cs) | Independent read/write/saved-result permissions, archived and hidden children, Stage/shared Save, seed-aware IDs, dirty targets and rollback |
| 5. Testing | [RelationalContractFixture](RelationalContractFixture/README.md) | [ContractNotesRepositoryDesigner](RelationalContractFixture/Configuration/ContractNotesRepositoryDesigner.cs) | [Real operations](RelationalContractFixture/Generated/Repositories.g.cs) + [automatic fixture](RelationalContractFixture/Generated/TestDoubles.g.cs) | One application-port contract suite on generated fakes and real SQLite, with explicit case reset |
| 5. Testing | [RelationalWrites](RelationalWrites/README.md) | [FixtureTicketsRepositoryDesigner](RelationalWrites/Configuration/FixtureTicketsRepositoryDesigner.cs) | [Ticket/audit ports](Contracts/Generated/FixtureTicketsRepositories.g.cs) + [real operations](RelationalWrites/Generated/Repositories.g.cs) | Actual generated IDs, expected versions, shared staged save and cross-repository rollback |

`Simple` and `Configured` are enough to understand ordinary use. Read the other examples only
when their optional features match your application. CQRS selectors deliberately expose model
classes; closed operations expose EF-free DTOs and named methods. These are different API choices,
not steps that every consumer must adopt.

## How a designer becomes these files

A designer is not discovered or executed by the consumer's `Program.cs`. The
[GenerationRunner](GenerationRunner/Program.cs) explicitly pairs each designer with an EF context,
then chooses the file destinations. The designer's class name does not determine the filenames.

For example, the runner pairs `LookupDirectoryContext` with `LookupDirectoryRepositoryDesigner`.
Its generation is equivalent to this code (paths are relative to `samples/ConfigurationExamples`):

```csharp
var design = new LookupDirectoryRepositoryDesigner().CreateModel();
var bundle = RepositoryGenerator.Generate(directoryContext, design);
bundle.Write(new RepositoryOutputLocations(
    "AccessorAdapters/Generated/Repositories.g.cs",
    Projections: "AccessorAdapters/Generated/Queries.g.cs",
    Manifest: "AccessorAdapters/Generated/manifest.json"));
```

`directoryContext` is a design-time `LookupDirectoryContext`. Its EF model supplies keys, indexes,
converters and relationships; the designer selects the generated API and behavior. For this example,
`UseValueConverter<Guid>()` yields `ById(Guid id)`, and `HasQuery("ListEntries")...List()` yields
`ListEntriesAsync()`. See the [complete declaration-to-output walkthrough](AccessorAdapters/README.md).

There are three different kinds of files to keep apart:

- `Configuration`, `Model` and each example's `Contracts`: handwritten generation inputs.
- `Generated/*.g.cs`: emitted persistence, selectors and sometimes query helpers. Closed API
  interfaces are emitted separately under the shared `Contracts/Generated` project so they stay EF-free.
- Consumer `Program.cs`: exercises those outputs; it neither creates their API nor runs a designer.

The forty-three EF-free testing examples additionally emit `Generated/TestDoubles.g.cs`; ordinary production output
lives under `Persistence/` and is excluded from their EF-free consumer executables. ScriptedPorts,
ScriptedStreams, MemoryFixtures and MemoryPartitions arrange outcomes explicitly; storage alone
does not execute operations. MemoryCriteria, MemoryCoverage, MemoryJoins, MemoryProxies, MemoryNavigation, MemoryProxyNavigation, MemoryManyToMany and MemoryOptionalLinks separately opt into portable designer
reads, with explicit coverage/override receipts rather than a universal SQL/EF simulation.
MemoryOptionalLinks distinguishes nullable source FKs from nullable targets: target filtering is
explicit in the shared designer, while unguarded SQL null propagation retains a typed override.
MemoryRelationshipComparers independently selects comparison for a principal-key component on fixture
reference/collection reads. Both directions share it; query predicates, store identity and writes stay unchanged.
MemoryNullTargets separately preserves missing optional targets for explicit null-safe counts,
presence checks and scalar/DTO projections. This per-navigation opt-in does not authorize general
SQL propagation, unsafe dereferences or explicit entity loading.
MemorySharedWrites independently enables designed tracked writes, explicit version setters, shared
Stage/Save and checkpoint rollback on a deliberately selected fixture unit of work. MemoryBatches adds
bounded PatchMany/leaf DeleteMany with all-target preparation, alternate-key matching and ordered
results. MemoryGeneratedValues separately declares explicit scalar factories for IDs/defaults/computed
tokens. MemoryProxyWrites adds separate public scalar write adapters for internal models, without
executing entity constructors or inferring reverse DTO projections. MemoryKeyIdentity independently
configures fixture key comparison while demonstrating that query predicates and provider collation
are not rewritten. MemoryImmediateWrites explicitly opts suitable immediate scalar Update/Delete and
returning/consume commands into fixture execution without flushing pending writes. MemoryAtomicWrites
extends that explicit opt-in to scalar insert-if-absent/conditional-upsert definitions, with all-saved-row
conflicts and update-only visibility. MemoryCollections separately opts designed bounded child reconciliation
into separate fixture stores, reusing production ownership, add/update/missing-item and version rules.
MemoryLinks explicitly applies those rules to two-FK join rows, with complete immutable target
reference keys and payload updates; related entities are not created, updated or deleted implicitly.
MemorySharedLinks separately opts into compatible overlapping FK columns, with owner consistency
checked before collection loading/mutation; it adds no tenancy, authorization or SQL constraint simulation.
MemoryAdditionalReferences explicitly names every FK role of a multi-reference join, with complete
Add-only scalar mappings and immutable matched references; referenced entities are never written.
MemoryOptionalReferences separately declares nullable additional FK roles, with complete all-or-none
creation values and immutable matched presence/absence. The matching target remains required.
CompositeCollections extends those same optional rules to complete ordered matching combinations,
with generation-time uniqueness/protection checks and early null-component rejection.
MemoryCreateBatches independently declares bounded scalar creation without artificial matching keys,
with all-candidate preparation, generated-ID reservations and fresh input-ordered DTOs.
MemorySavedResults explicitly queries fresh related DTOs after saving rather than returning a tracked/seeded graph.
MemoryBatchResults extends that optional mode to bounded patch batches, using one saved-result read and input ordering.
Generated values, arbitrary graph behavior and provider guarantees are not inferred.
RelationalContractFixture compiles both implementations and shares the same application-port cases;
RelationalWrites uses only real production output. Both deliberately reference the optional EF
fixture harness and a selected provider. There is no new generation flag or dependency for ordinary users.

When a designer binds an application-owned interface such as `ISalesReports`, the generated read
interface inherits it rather than copying its methods. Look in the linked implementation for the
generated method bodies. The per-example maps identify both files and the relevant symbols.
Manifests describe the selected configuration; optional API snapshots are candidates, not approved baselines.

## A consistent folder layout

For example:

```text
AccessorAdapters/
  README.md                                      start here
  Configuration/LookupDirectoryRepositoryDesigner.cs  generator settings and operations
  Model/LookupDirectoryContext.cs                 context, entities and EF conversion
  Contracts/DirectoryContracts.cs                 handwritten application types
  Program.cs                                      executable consumer demo/assertions
  Generated/                                      checked-in generator output
  AccessorAdapters.csproj                         runnable consumer project
```

`Simple` does not need handwritten DTO/port contracts. `MultiContext` has one designer for each
context. The two adapter examples now have separate designer, model and contract files, not a
shared source file whose relevant configuration is hidden further down. Public namespaces and
generated output locations are unchanged.

## Why there are shared build projects

Source folders are organized by example. Assembly dependencies remain organized by responsibility:

| Build-support project | Sources it compiles | Why it is separate |
| --- | --- | --- |
| [Models](Models/Models.csproj) | Each example's `Model/*.cs` | EF models are available before persistence is generated |
| [Contracts](Contracts/Contracts.csproj) | Each example's handwritten `Contracts/*.cs`, plus `Contracts/Generated` outside bootstrap | Application contracts have no EF/model/designer reference |
| [Designers](Designers/Designers.csproj) | Each example's `Configuration/*.cs` | Design-time configuration does not become a runtime dependency |
| [GenerationRunner](GenerationRunner/Program.cs) | The code-first generation coordinator | Uses contexts/designers without referencing consumer persistence projects |
| [GenerationInputs](GenerationInputs/GenerationInputs.csproj) | Optional compilation-reference exports | Supports target-aware validation; it is not another application |

The runnable projects import [ExampleSources.props](ExampleSources.props), which displays the
local input files but excludes them from consumer compilation. The three input projects compile
those files through linked source items instead. This avoids duplicate types, keeps the first-
generation bootstrap independent of generated repositories, and retains the EF-free contract boundary.
You do not need all these support projects in a small real application.

## Generate, check and run

Run everything from the RepoGen repository root:

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run
```

The script builds the independent C# runner, regenerates outputs, builds all eighty-one applications
and runs their assertions: seventeen use SQLite (one also checks a fake), seventeen use EF-free scripts/fixtures. Omit `-Run` to regenerate/build only. The
runner selects each designer; configuration is in those designers, not a large set of CLI flags.
The script uses the pinned SDK, consumers target .NET 8, and its default configuration is Debug.
Restore uses cached packages when feeds are unavailable and skips NuGet audit for this demo run.

To check freshness and execute the examples without rewriting generated output:

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run
```

Optional compiler validation uses actual .NET 8 references and a separately compiled EF-free
contract boundary, plus independent EF-free test-double compilation where enabled. It is not required for ordinary generation:

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Check -Run -ValidateCompilation
```

Or run a single consumer using its checked-in generated code:

```powershell
dotnet run --project samples/ConfigurationExamples/AccessorAdapters -c Release
```

Running a consumer does **not** execute its designer or regenerate files. After editing
`Configuration`, `Model` or handwritten `Contracts`, run `Generate.ps1` first. Generated files are
checked in for easy browsing and direct builds; edit the inputs, not `.g.cs` files. Generation
never accepts or overwrites the Ledger approved API baseline automatically.

The solution groups these same projects under Basics, Contract shapes, Queries and loading,
Writes and infrastructure, Testing, and Build support. Existing project paths/run commands are preserved.

See [Generation options](../../docs/implemented/generation/GENERATION-OPTIONS.md),
[Code-first generation](../../docs/implemented/generation/CODE-FIRST-GENERATION.md) and
[Implementation progress](../../docs/other/IMPLEMENTATION-HISTORY.md) for APIs and boundaries.
