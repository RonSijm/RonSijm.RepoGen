# Target application in-memory repositories: feature map and generator requirements

This is a research inventory, not a current API guide. The original source findings are preserved;
generic requirements do not promise replication of every domain-specific behavior.
See [Implemented features](../implemented/README.md) for available configuration.

Research date: **2026-10-07**. Target Application is a generic label for the source reviewed.
Private application identifiers and local source links are omitted for publication; behavioral
findings and inventory counts are retained. This is a static behavioral inventory and a generic
generator plan, not an application migration. No target-application files were changed, and no
target-application builds, tests or database operations were run.

Related: [overall roadmap](../future/REPOSITORY-FEATURE-PLAN.md), [implementation progress](IMPLEMENTATION-HISTORY.md).
**F15 is delivered in bounded slices**: scripts/shared stores, portable single/multi-source reads,
public scalar read/write maps, declared navigation reads (including
[required explicit many-to-many joins](../implemented/testing/INMEMORY-MANY-TO-MANY.md)), tracked scalar/batch writes, explicit value
factories, Stage/Save/checkpoint rollback, real-provider fixtures and per-command immediate scalar
write/insert/upsert simulations. [Explicit child-store reconciliation](../implemented/testing/INMEMORY-COLLECTIONS.md) now
reuses designed owner-scoped leaf-child add/update/missing/version rules, including
[composite matching keys](../implemented/writes/COMPOSITE-COLLECTION-KEYS.md). Explicit
[saved DTO/navigation results](../implemented/writes/SAVED-COMMAND-RESULTS.md) now read fresh saved tables after the shared
save. Broader graph shapes and additional value/provider policies remain extensions; no arbitrary
entity behavior or SQL parity is inferred. The Target Application inventory below is unchanged by these deliveries.

## 1. Findings at a glance

Generating optional test doubles would remove substantial duplicated test infrastructure. The useful target is **the same configured application contracts with a separate testing backend**, not an automatically equivalent database.

The existing implementations fall into three overlapping categories:

- **Stateful fakes:** dictionaries/lists, seeding, filtering, projections and mutations.
- **Scripted/recording doubles:** arranged results/exceptions, call counters, argument history and asynchronous hooks.
- **Placeholders:** throw-only methods, or constant null/empty/success responses without the advertised behavior.

Do not treat all three as complete in-memory persistence implementations. Many also contain clinical/financial decisions, migration mapping or external-service behavior that should stay handwritten.

### Inventory and counting rules

| Area | Repository classes/files | Direct method declarations | Throw-only methods |
| --- | ---: | ---: | ---: |
| Application test infrastructure | 46 | 402 | 171 |
| Legacy datalayer test infrastructure | 26 | 468 | 288 |
| ServiceUtilities test infrastructure | 2 | 23 | 3 |
| Local backend/client/shared test doubles | 3 | 78 | 68 |
| **Total** | **77** | **971** | **530** |

**16 classes have only throw-only methods.** The other **441 method bodies are not necessarily complete implementations**: they include helpers, constant responses, delegated business logic and partially unsupported branches.

Counts include private/protected helpers and explicit-interface methods declared directly on each repository class. Constructors, properties, interface declarations and inherited methods are excluded. “Throw-only” means the body consists only of throwing `NotImplementedException` or `NotSupportedException`; other deliberate validation exceptions are not counted as placeholders.

Discovery combined repository/in-memory filename patterns, repository class/record declarations across the tree, and repository declarations in all three in-memory framework projects. Build outputs were excluded. The filename scan found 80 files: 75 implementation files, four characterization-test files and one registration extension. Declaration discovery added two implementations hidden in differently named files:

- x509CertificateRepositoryInMemory in `BlobstorageCertificateServiceInMemory.cs`.
- SignalRRepositoryInMemory inside `MessagingControllerTests.cs`.

The inventory below covers all 77 discovered implementations. Non-throw-only bodies were inspected for state, queries, writes and test seams; throw-only members were classified as unavailable. Representative tests and supporting stores/registration/base classes were inspected as additional evidence. This does not prove parity with the production repositories or audit every inherited behavior.

## 2. Generic feature map

IM IDs below identify testing requirements; F IDs refer to the existing overall roadmap. Existing designer query/write features are inputs to this work, **not blanket promises of in-memory support**. The delivered portable read subset and remaining requirements are distinguished in the delivery section below.

| ID | Feature found in Target Application | Representative evidence | Generic RepoGen requirement |
| --- | --- | --- | --- |
| IM01 | Production ports plus arrange/assert interfaces; one fake implements several ports | Patient; SSO; appointment-type test-helper interface | Generate optional test-only fixture interfaces and exact implementations of selected closed/CQRS or bound ports. Preserve overloads, value-object inputs, nullability and result types. Test helpers must not expand production contracts. F02 + F15.1. |
| IM02 | Shared stores and scope isolation | Shared entity dictionaries; patient consent and practice consent; V2 settings scopes | Default to one isolated store per test fixture/context key, shared by participating repositories. Read/write/arrange aliases resolve to the intended same implementation. Cross-request singleton fixtures require explicit lifetime configuration. F03/F04/F13 + F15.2. |
| IM03 | Seed one/range, replace, clear and inspect | Patient, staff, location, export jobs, consent maps and SSO stored codes | Typed seed/replace/reset/snapshot helpers. Specify duplicate handling, restore-to-baseline versus empty reset, counter reset and whether scripts/history are reset. Do not expose mutable backing dictionaries by default. F15.2. |
| IM04 | Integer IDs, honest IDs, composite keys, generated IDs and duplicate validation | Appointment/task counters; consent (partner, patient); scheduler (type, ID); declaration file (sale, type) | Reuse key/type adapters; deterministic seed-aware generators and configurable key comparers. Generated test IDs must not invent business numbering or claim database identity parity. F02/F09 + F15.2/F15.4. |
| IM05 | Clone-on-seed/save versus live-reference behavior | Appointment-type JSON clone; task custom ID converters; invitation insert/update clone; V2 settings clone | Explicit copy/isolation policy with safe snapshots by default and a custom clone adapter for unsupported shapes. Distinguish input mutation, detached reads, staged writes and fixture inspection. Never assume arbitrary graphs/value objects can be JSON-cloned. F15.2. |
| IM06 | Typed criteria, optional filters, membership, ranges and visibility | Location/staff/invitation ApplyCriteria; procedure criteria; scheduler dates; administration inactive flag | Compile the portable designer query model for both backends. Reuse typed requests, fragments, membership/null/empty policies, input guards, sorting, projections and terminals. Do not infer predicates from request-property names or automatically import existing ApplyCriteria methods. F05-F07/F13 + F15.3. |
| IM07 | Multiple datasets, navigation traversal, joins and nested projections | Consent statuses; invitation-to-call linking; patient contact/summary mapping; InternetAgenda staff/procedure views | Shared source tables and declared relationship/key mappings. Support an explicit portable subset of joins, subqueries and nested projection/load rules, with validation of absent relationships. Do not fabricate a domain aggregate or silently ignore include/load policies. F05/F08 + F15.3. |
| IM08 | Create/update/remove, range updates and saved projections | Treatment rules, tasks, invitations, materials, CDM patients, job/status writes | Execute approved designer write mappings against the store, returning the configured shapes/outcomes. Keep add/upsert/replace/patch/ignore-missing/throw-if-missing semantics explicit. Do not derive reverse mappings from a DTO projection. F09 + F15.4. |
| IM09 | Shared state is present; coherent stage/save/rollback simulation is largely absent | Dictionary writes generally occur immediately; related tables share one store | Planned unit-of-work simulation: pending versus committed state, explicit Stage/Save and rollback across repositories. This is required by RepoGen's current contracts even where the handwritten fakes do not provide it. F04/F09 + F15.4. |
| IM10 | Compare-current-state update and consume/read-delete | Export job expected-status update; SSO consume | Portable configured predicates/outcomes and explicitly arranged conflicts/not-found results. Version/uniqueness simulation can be opt-in; it is not a rowversion, lock, transaction or race-correctness test. Suitable immediate scalar Update/Delete/returning and InsertIfAbsent/InsertOrUpdate commands now have explicit per-command simulation; unsupported shapes and actual provider guarantees still need scripts/real-provider fixtures. F10/F11 + F15.4. |
| IM11 | Fixed responses, per-key responses, exceptions and repeated failure | Duplicate-settings response map; debtor trigger; booking result/failures; outbox exception; photo failures | Typed per-operation handlers and response/exception sequences, including async delegates and argument matching. Unexpected calls fail clearly by default. Do not generate silent success/empty results for unimplemented members. F15.1/F15.5. |
| IM12 | Call counts, last arguments, ordered history and published messages | Export/file/photo doubles; outbox; SignalR | Optional recording with request snapshots, invocation/result/error/cancellation outcome, bounded history and reset. Use typed overload identities, not fragile method-name strings. Records must not be changed by later mutation of caller arguments. F15.5. |
| IM13 | Cancellation checks, live clocks, async hooks and tiny delays | Appointment-type/treatment-plan/procedure checks; ZIP hook; UTC task/photo/time-off logic; tariff delay | Consistent cancellation for generated operations, deterministic time/ID dependencies where configured, and controllable async completion/failure hooks. Keep domain time rules outside RepoGen; do not introduce artificial sleeps or background scheduling. F03 + F15.4/F15.5. |
| IM14 | Scope-keyed fallback/default settings and initialize-on-read behavior | Legacy complex settings versus V2 application/user/location/administration stores and inherited base | Scope/key selection is generic. Fallback values, serialization, encryption, feature flags and initialize-on-read rules require explicit designer operations/adapters or handwritten application code, not a built-in settings engine. F03/F07/F13 + F15.2/F15.5. |
| IM15 | Partial implementations, ignored inputs and unsupported branches | All inventory gaps below, including 530 throw-only methods | Deterministic capability/coverage receipt per operation: Generated, RequiresOverride or Unsupported. Reject unsupported generation unless an explicit handler/override is provided; runtime unexpected invocation must identify the operation. No false claims of complete coverage. F14 + all F15 slices. |
| IM16 | Fakes themselves have characterization tests | Four repository-test fixtures listed below | Shared portable-operation contract suites against fake and real EF implementations, plus independent provider-specific tests. Focused RepositoryDesigner examples must show configuration, exact generated output and usage. F14 + F15.6. |

### Criteria support: reuse the definition, not a second query language

The important criteria shapes include:

- Optional “include inactive” and nullable deleted-state filters.
- Optional ID, ID collection and nested-related-date selection.
- Compound patient-or-ID validation, explicit null/empty membership behavior.
- Job-type/enabled flags and optional date bounds.
- Multiple strongly typed IDs, enums and nested graph projections.

RepoGen already supports designer-defined typed requests/predicates/fragments. F15 should reuse that model. A service should keep calling a named method such as `SearchAsync(criteria)`, with the same application criteria type in production and tests. It should not assemble a different selector/filter DSL for the fake.

Any existing custom `ApplyCriteria` implementation needs explicit configuration or a typed override. A method body accepting criteria but ignoring some fields is **not** a reason to generate those omissions.

### What is observed versus proposed

This scan found real grouping/de-duplication and ordering in InternetAgenda, scalar/exists/dictionary/list projections elsewhere, and several range writes. It did **not** establish working in-memory pagination/reporting aggregates, complete change tracking, transaction isolation, foreign-key enforcement or concurrency-token handling. For example, the task paged-read method throws.

Counted pages, grouped aggregates, cursor rules, default-sort control, stage/save and expected-version outcomes are already relevant to RepoGen's generated production APIs. Their testing-backend support belongs on the roadmap even when the current Target Application fakes lack it. They must be bounded by an explicit capability matrix rather than assumed portable.

## 3. Supporting test infrastructure

These are part of the feature map but are **not additional repository implementations** in the 77-class count.

| Supporting component | Observed role | Consequence |
| --- | --- | --- |
| EntityFrameworkDatabaseInMemory | Dictionary tables for patients, information, treating staff, staff, locations, invitations/calls, material, DPSI and removal reasons; constructor seeds two reason values; lives with internal database entities. | Despite its name, this is a hand-rolled store, not EF's InMemory provider or a DbContext. Generate optional shared store/test fixtures without forcing EF into application contracts. Consumer lookup seeds remain consumer-owned. |
| ConnectInMemoryConsentStore | Practice-consent set and partner/patient-consent map used together. | Relationships span repositories; independently generated isolated dictionaries would change behavior. |
| Connect test factory | Explicit singleton consent store for API tests spanning request scopes. | Lifetime must be configurable; a singleton is a fixture choice, not the default process-global state. |
| Scoped/singleton aliases | Register concrete instance then resolve service aliases to it; optional initialization action. | Generate deliberate test registrations without replacing production registration by accident. |
| Settings registration extension | All inherited settings interfaces resolve to the same scoped implementation. | Multi-port aliases must preserve instance identity and state. Prefer explicit selected interfaces over blindly registering everything. |
| Appointment-type arrange interface | Store helper separate from production read interface. | Optional test fixture contracts separate arrange/assert from the code under test. |
| Task ID converters | Custom value-object serialization, with analogous task/patient/task-type converters. | Copying/adaptation needs explicit support; JSON round-tripping is not a universally safe default. |
| SettingsRepositoryBase | Application defaults, encryption, validation, feature switches and some read-time initialization wrap protected persistence methods. | Reuse application behavior around a narrow fake port; do not copy the whole settings engine into a generic generator. Inherited methods are outside the direct-method counts. |

Representative characterization fixtures read, not executed:

- Settings scopes and get/change round-trips.
- Staff criteria, function mapping and nested arrangement.
- Patient removal reasons, missing results and SMS phone filtering.
- Invitation related-date list/exists selection and Store behavior.

## 4. Complete per-repository inventory

M/S = directly declared methods / throw-only methods. All other unavailable operations are counted in S; partially unsupported branches and constant/no-op behavior are noted separately. Rows identify source behaviors, not promises to reproduce those domain behaviors in RepoGen.

### 4.1 Application repositories

| ID | Repository | M/S | Implemented or arranged behavior | Gaps / limits |
| --- | --- | ---: | --- | --- |
| A01 | DeclaratieMedewerkerRepositoryInMemory | 2/0 | Replace seeded list; retrieve by staff-ID membership, adapting value-object IDs to integers. | No writes or database save semantics. |
| A02 | DeclaratiePatientRepositoryInMemory | 2/0 | Replace seeded list; retrieve by patient-ID membership. | No writes or database save semantics. |
| A03 | DeclaratieRepositoryInMemory | 3/1 | Replace and return a seeded list of factoring-file results. | The read ignores date/administration arguments; naming operation throws. |
| A04 | DeclarationFileRepositoryInMemory | 6/0 | Public dictionary keyed by (sale ID, file type); nullable lookup, contract-number projection, save/overwrite and remove; generate file paths/names. | Paths and third-party/double-save responses simulate blob storage, not actual file persistence. |
| A05 | AfspraakMigrationRepositoryInMemory | 34/34 | No working bodies: scheduling/history, reads, modifications, sets/series and overlap/business-rule methods all throw. | A large contract placeholder, not evidence that these behaviors have been implemented. |
| A06 | AfspraakRepositoryInMemory | 6/2 | Seed migration wrappers; allocate/resynchronize integer IDs; two create overloads map selected fields into a private store; appointment-to-location lookup throws when missing. | Change and retrieve-data methods throw; embedded test-helper port exposes seeding. |
| A07 | AfspraakSoortRepositoryInMemory | 2/0 | Clone seeded appointment types using JSON; optionally include removed rows, sort by sequence number, check cancellation. | Clone-on-seed is not clone-on-every-read; separate arrange interface. |
| A08 | AgendaRepositoryInMemory | 14/12 | Seed and return a public list of appointment-type DAOs. | Other agenda/chair/work-kind operations throw; returned list is live. |
| A09 | AnamneseExportRepositoryInMemory | 1/0 | Latest-anamnesis method returns null. | Constant-response stub: no state or latest-row selection. |
| A10 | BehandelplanRepositoryInMemory | 2/0 | Seed a list; filter treatment plans by patient; check cancellation. | Read-only fake with supplied object references. |
| A11 | BerichtenServiceRepositoryInMemory | 8/8 | No working bodies: message/notification CRUD methods throw. | No message transport, storage, or recording implemented. |
| A12 | PatientConsentRepositoryInMemory | 6/0 | Seed/clear/inspect partner-patient consent map; add/overwrite, remove, existence, partner statuses joined to practice consents with missing status false. | Shares Connect store with practice repository; helper methods are separate from production port. |
| A13 | PraktijkConsentRepositoryInMemory | 6/0 | Seed/clear partner set; add, remove-if-present, existence and list all. | Shares Connect store; removing practice consent does not cascade patient-map entries. |
| A14 | SsoContextRepositoryInMemory | 4/0 | Seed/clear/inspect codes; overwrite stored context; consume by code only when partner matches, returning and removing it. | License argument is not validated; dictionary consume is not proof of atomic database behavior. |
| A15 | ContactMomentRepositoryInMemory | 14/7 | Seed dossier IDs; add contact/message graphs after dossier validation; map both directions; inspect all contacts. | Seven methods throw; dossier validation and message construction contain consumer-specific rules. |
| A16 | DentalFitnessRepositoryInMemory | 9/1 | Seed/replace one result per patient, range seed, remove/clear; create/update/read; changed-since and expiry-window selection. | One FHIR method throws; expiry logic is domain-authored and clock-sensitive. |
| A17 | DossierExportFileRepositoryInMemory | 5/0 | Record save-image count and last arguments; record ZIP count; await a programmable hook during ZIP. | Export save is a no-op, existence is constant true, token is canned; no actual file/ZIP backend. |
| A18 | DossierExportRepositoryInMemory | 15/0 | Seed jobs/items/items-by-job; three identity counters; create and retrieve jobs/items/details; status filters/counts; expected-status update; record changed statuses and created details. | Active-item check is constant false; independently seeded item indexes are not maintained consistently by all creates. |
| A19 | DossierMateriaalRepositoryInMemory | 3/0 | Material, category and material/category tuple reads return empty arrays. | Constant responses, not a material store. |
| A20 | DpsiRepositoryInMemory | 8/4 | Seed application/internal entity forms into shared store; filter patient PPS scores and project enums. | Latest-DPSI result is constant null; four mutation/read operations throw NotSupportedException. |
| A21 | ExquirayFileFotoRepositoryInMemory | 2/0 | Record patient/type download calls; configure failing photo types; return canned success/size results. | No real file download. Generic recording/failure seams are reusable; imaging protocol is not. |
| A22 | ExquirayFotoRepositoryInMemory | 8/4 | Seed latest photos by (patient, type) plus other-photo list; inspect calls/types; lookup latest; filter other photos by patient/excluded types/recent date, ordered newest first. | Four operations throw; recency uses live UTC time. |
| A23 | DentalFitnessFhirRepositoryInMemory | 1/1 | No working body: FHIR observation read throws. | FHIR response construction is not supplied. |
| A24 | MedewerkerFhirRepositoryInMemory | 4/0 | Seed a privately owned entity store; retrieve staff sync DTOs with roles, selected email and ZorgMail-address construction. | Administration argument is unused; this repository does not share the injected store used by other staff fakes. |
| A25 | JournaalRepositoryInMemory | 1/1 | No working body: journal read throws. | Contract placeholder. |
| A26 | LocatieFhirRepositoryInMemory | 1/1 | No working body: FHIR location read throws. | Contract placeholder. |
| A27 | LocatieRepositoryInMemory | 13/3 | Seed/map locations into shared store with IDs; clear/count; active and ID-set reads; criteria-based IDs/full DTOs via ApplyCriteria; added/changed wrappers replace rows. | Three methods throw, plus unsupported modification branch; null/empty IDs can mean all; clear does not reset identity counter. |
| A28 | MateriaalRepositoryInMemory | 11/7 | Add/map material with generated ID/default fields; physical remove returns removed item or null; count and test inspection. | Seven operations throw, including normal read/update/category/default paths. |
| A29 | MedewerkerRepositoryInMemory | 35/12 | Seed several input forms; clear/inspect; allocate IDs; add nested functions; reuse criteria; role/function/member/single/list/reference projections; changed/new wrappers and nested removed administrations. | Twelve methods throw; some name/function reads are constant empty and Zivver lookup ignores requested identity; load options are not fully modeled. |
| A30 | OrthodontieMigrationRepositoryInMemory | 1/1 | No working body: latest orthodontic plan read throws. | Contract placeholder. |
| A31 | OutboxMessageRepositoryInMemory | 3/0 | Record single/range published messages; configurable exception before publishing. | An interaction fake, not transactional outbox persistence or delivery. |
| A32 | ParodontiumStatusRepositoryInMemory | 7/5 | Seed and lookup latest status by patient. | Five other reads/writes throw NotSupportedException. |
| A33 | PatientDossierExportRepositoryInMemory | 2/0 | Seed profile by patient key; nullable export-profile lookup. | No automatic profile construction. |
| A34 | PatientRepositoryInMemory | 70/28 | Five ports on one fake; seed application/migration/internal entity forms; shared patient/info/treating-staff tables; clear/remove; ID/BSN/exists/deceased and contact/scalar/dictionary/summary projections; treating-staff insert/lookups; CDM add/update and information/group reads; active/created-after dental-fitness DTOs. | 28 methods throw; profile read is constant null; handwritten graph mapping has unsupported non-binary-gender branch; some load/cancellation arguments are ignored. Clinical/migration mappings are not generic conventions. |
| A35 | PolisFhirRepositoryInMemory | 2/2 | No working bodies: FHIR/sync policy reads throw. | Contract placeholder. |
| A36 | JobSchedulerRepositoryInMemory | 6/0 | Concurrent dictionary with (job type, job ID) key; seed/create/update enabled flag; missing lookup throws; criteria filter type/enabled/scheduled-to/last-run with optional bounds. | Concurrency-safe container operations do not establish transaction or database-lock equivalence. |
| A37 | TariefRepositoryInMemory | 2/0 | Return two hard-coded tariff-code lists. | Consumer fixture defaults, not a configurable tariff database. |
| A38 | UitnodigingRepositoryInMemory | 16/6 | Seed/allocate IDs in shared store; clone inserted/updated inputs; single and range updates; link invitations to calls; reuse criteria for list and existence; map related call fields. | Six methods throw; missing linked call throws; range replacement and single-update missing-row behavior differ. |
| A39 | VecozoRepositoryInMemory | 8/8 | No working bodies: insurance checks, lookups and result updates throw. | External-service/insurance workflow placeholders. |
| A40 | VerkoopRepositoryInMemory | 5/5 | No working bodies: sale reads/status/update methods throw. | Contract placeholder. |
| A41 | VerrichtingExportRepositoryInMemory | 1/0 | Procedure export read returns empty list. | Constant-response stub. |
| A42 | VerrichtingRepositoryInMemory | 13/4 | Seed procedure and separate validation lists; require patient or ID criteria, check null/cancellation; patient/ID/code filters; ordered status projections with element-mask mapping; validation/history/tariff reads. | Four operations throw; some validation/history reads ignore patient/date arguments; distinct seed stores can diverge. |
| A43 | VerzekeraarMigrationRepositoryInMemory | 6/5 | List-insurers read returns empty list. | Five lookup/mutation methods throw. |
| A44 | VrijeTijdRepositoryInMemory | 6/0 | Allocate IDs for default-ID seeds; create, remove, single/member/range reads; range selects rows fully contained in interval. | Store relies on live references for updates and does not add nondefault-ID inputs; create requires exactly four appointment IDs, uses UTC time and fixed audit/timestamp values. |
| A45 | ZorgplanItemRepositoryInMemory | 2/0 | Both latest-care-plan-item reads return empty lists. | No state, ordering or latest selection. |
| A46 | TaakRepositoryInMemory | 26/9 | Clone seeds/creates/updates with custom honest-ID JSON converters; duplicate checks; default-ID counter; single/member/patient/staff/existence reads; task-type seed/lookup; remove; range and orthodontic updates; create completed orthodontic task. | Nine methods throw, including paged reads; return-reference behavior varies; ID counter is not advanced by all explicit seeds; clinical status/time construction stays handwritten. |

### 4.2 Legacy repositories

| ID | Repository | M/S | Implemented or arranged behavior | Gaps / limits |
| --- | --- | ---: | --- | --- |
| L01 | AdministratieRepositoryInMemory | 11/7 | One implementation of legacy/modern ports; seed by key; active check; missing single throws domain error; map wrapper; optional criteria include inactive administrations. | Seven methods throw. Criteria semantics are authored, not inferred from names. |
| L02 | AfspraakRepositoryInMemory | 34/32 | Seed appointments by key; retrieve an ID set, optionally throwing when requested count does not match. | 32 methods throw; load options ignored; duplicate requested IDs can affect count validation. |
| L03 | BehandelRegelRepositoryInMemory | 6/0 | Replace dataset; add with duplicate-key rejection, remove, nullable single, patient/dossier list, update range only for existing keys. | Returns supplied references and returns the input update array even for missing rows. |
| L04 | BijzondereTandheelkundeRepositoryInMemory | 3/3 | No working bodies: procedure/time-addition reads/update throw. | Clinical workflow placeholder. |
| L05 | DebiteurRepositoryInMemory | 11/7 | Seed/replace debtor by key; retrieve ID membership; configure exception by trigger. | Seven methods throw; load options ignored; failure trigger is a handwritten string. |
| L06 | DeclaratieRepositoryInMemory | 20/18 | Inject wrapped real implementation; delegate declaration scan to it. | 18 methods throw; delegated scan is explicitly identified in source as non-database business logic. |
| L07 | EmailRepositoryInMemory | 1/1 | No working body: email-address retrieval throws. | Contract placeholder. |
| L08 | FixDuplicateSettingsRepositoryInMemory | 3/0 | Arrange a duplicate-settings response by (license, setting scope); retrieve it or empty array. | Scripted lookup; it does not actually detect duplicates in stored settings. |
| L09 | GrootboekRekeningRepositoryInMemory | 1/1 | No working body: balance-account renumbering throws. | Financial transformation placeholder. |
| L10 | InfomedicsRepositoryInMemory | 7/5 | Replace seeded contracts; list by administration relation ID. | Five contract/lookup/write methods throw. |
| L11 | IntellyRepositoryInMemory | 2/2 | No working bodies: upload and availability methods throw. | External-system adapter placeholder. |
| L12 | InternetAgendaRepositoryInMemory | 37/25 | Seed appointment types, staff, locations, chairs, patient procedures; single/list lookups; active staff/function filter, grouping de-duplication and name sort; wrapper mappings; initialize nested function timestamps; patient procedure filtering against active staff. | 25 methods throw plus check-in-use chair branch; multi-dataset views are assembled manually and use domain function definitions. |
| L13 | JournaalRepositoryInMemory | 3/3 | No working bodies: book reminders, list runs and reminder information throw. | Financial workflow placeholder. |
| L14 | MijnMedicijnRepositoryInMemory | 10/10 | No working bodies: invitations, practice information, counts, updates and planned appointments throw. | External-service/application workflow placeholder. |
| L15 | OproepRepositoryInMemory | 19/6 | Seed positive-ID calls/invitations/processed invitations; inspect calls; insert with counter; exists/single; processed invitations by call; related patient/function invitation projection; create invitation; arrange booking result and repeated failures; expose call-history property. | Six methods throw; 'today' projection ignores utcNow; default failure repeat -1 does not actually trigger throwing in current body; booking is scripted, not reimplemented. |
| L16 | OrthodontieRepositoryInMemory | 30/21 | Seed diagnoses/action points; retrieve diagnosis/action by ID and patient; status by treatment-rule IDs; model-cabinet and plan lookups over private lists; separate test-helper interface. | 21 methods throw; several backing lists have no public arrangement method. Clinical assignment/update rules are not implemented. |
| L17 | PatientRepositoryInMemory | 32/29 | Seed patients; retrieve ID membership; key-existence validation; implements multiple ports and arrange interface. | 29 methods throw; load flags ignored; no graph loading or business-rule reproduction. |
| L18 | PeilStationsClientRepositoryInMemory | 4/1 | Seed registration by AGB key, reject duplicate seeds; lookup returns zero when missing; send returns true. | Sampling-info method throws; no transport or call recording. |
| L19 | PeilStationsRepositoryInMemory | 2/1 | Return fixed AGB code. | Sampling-data method throws. |
| L20 | SettingRepositoryInMemory | 113/82 | Seed typed complex/settings/template collections; append nested call settings; application/relation-scoped expandable settings; paired get/change for declaration, individual declaration, WLZ, factoring, Payt, SMS, Intelly, call settings, surcharge, layout; retrieve Zivver/templates; clear complex map. | 82 methods throw; complex map ignores scope; missing reads can return a null Task; clear is partial; type-specific constructor defaults are consumer fixtures. |
| L21 | SettingRepositoryPortInMemory | 4/0 | Seed expandable settings by type/relation and upsert property values; generic complex-setting upsert keyed by scope/type/relation; single/list typed retrieval. | List retrieval ignores type/relation filters within its type bucket; obsolete legacy port, not a general settings engine. |
| L22 | SettingsRepositoryV2InMemory | 75/1 | Four stores: application type, user/type, location/type, administration/type; validate scope category/null; JSON-clone seeds; generic fallback-to-default reads; many typed get/change overrides; expose live application/location maps; reuse SettingsRepositoryBase. | One SMS implementation throws; inherited methods excluded from counts. Base supplies defaults, encryption, validation, feature flags and some initialize-on-read logic; those are application responsibilities. |
| L23 | UitnodigingRepositoryInMemory | 14/11 | Seed by ID; update many wrappers via mapper; retrieve by authentication GUID. | Eleven other updates/deletes/reads throw; stores object references. |
| L24 | VektisEIRepositoryInMemory | 5/5 | No working bodies: generate/process insurance interchange files throw. | External-file/business format placeholder. |
| L25 | VerkoopRepositoryInMemory | 11/9 | Append seed sales; filter by administration plus ID set; map wrappers with mapping context. | Nine methods throw; load options ignored. |
| L26 | VerrichtingRepositoryInMemory | 10/8 | Append seed procedures; ID membership using treatment-rule ID and wrapper mapping; implements three ports. | Eight other reads/declaration helpers throw. |

### 4.3 ServiceUtilities repositories

| ID | Repository | M/S | Implemented or arranged behavior | Gaps / limits |
| --- | --- | ---: | --- | --- |
| S01 | BackendTariefRepositoryInMemory | 13/2 | Wrap inherited tariff implementation with honest-ID/date adapters; expose code mapping, price/list/NZA/insurer variants; asynchronous per-code price list and wrapper projection. | Two methods throw; inherited pricing/storage behavior is not counted or a generic fake algorithm; initialization, prices and business calculations remain handwritten. |
| S02 | x509CertificateRepositoryInMemory | 10/1 | Separate staff/practice byte/password stores with composite string keys; materialize certificates on read; staff upload; seed/clear; generate self-signed certificates with configurable validity. | Remove throws and practice upload has unsupported branch; cryptography/certificate lifetime/storage protocol is external-adapter logic. |

### 4.4 Local test doubles

| ID | Repository | M/S | Implemented or arranged behavior | Gaps / limits |
| --- | --- | ---: | --- | --- |
| T01 | PeilStationsRepositoryInMemory | 2/1 | Integration-test-local duplicate: return fixed AGB code. | Sampling-data method throws. |
| T02 | SettingRepositoryInMemory | 73/67 | Seed and update Exquiray user settings and photo-type options; change Romexis/external-connection settings with null validation; initial unspecified photo types. | 67 methods throw; several get-only properties stay uninitialized; not a general client-settings repository. |
| T03 | SignalRRepositoryInMemory | 3/0 | Record publish-all/license messages in lists and identity messages in a keyed dictionary. | No network delivery; repeated identity key can throw because dictionary Add is used. Located inside controller test file. |

## 5. Behaviors to make explicit, not blindly reproduce

The following are source-observed limitations or discrepancies, not fixes applied to Target Application:

1. **Incomplete inputs:** factoring reads ignore date/administration; some staff/validation/history reads ignore supplied identities or periods; Oproep's “today” read does not use its UTC argument. Generate the declared predicate, not a weakened test-only predicate.
2. **Constant/no-op success:** latest export reads may always return null/empty, file existence is always true, sending sampling data always succeeds, and an export-active check always returns false. These are arranged scenarios, not repository algorithms.
3. **Aliasing varies:** task/settings/invitation writes sometimes clone; other seeds/reads return live references. Even clone-on-write fakes can return the stored object. A read consumer can accidentally mutate fake state without calling a write.
4. **Identity/reset varies:** some stores use max-plus-one, some counters, some count-plus-offset. Some explicit seeds do not advance counters; partial Clear methods leave other collections/defaults/counters intact. Make fixture reset and generated test-key rules deterministic.
5. **Indexes can diverge:** export job items and items-by-job are arranged independently; creating an item does not populate the latter. Prefer a common queryable source or explicitly maintained indexes.
6. **Missing-row semantics vary:** nullable values, validation exceptions, dictionary exceptions, `Single` exceptions and ignored missing updates all occur. Some missing paths even return a null Task instead of a completed Task with a null result. Match configured contracts/outcomes deliberately.
7. **Failure sequences are not reliable conventions:** Oproep's default repeat value of -1 is not handled as “always throw” by the current conditional; SignalR's identity recording can reject a repeated key. Generate typed, tested sequence/recording semantics instead.
8. **Load/filter policies are partial:** legacy load options are often ignored, while invitation criteria explicitly link related calls. A fake must not quietly lose required visibility/tenant/navigation rules.
9. **Immediate dictionary mutations do not represent a shared save:** do not equate these fakes with RepoGen's staged commands or transactions.
10. **In-memory execution is not SQL:** null comparisons, collation, ordering, translation, generated values, uniqueness, foreign keys, concurrency and provider roots can differ. A portable fake helps service tests; real-provider tests still verify database behavior.

### Deliberate exclusions

| Responsibility found in repository-shaped doubles | Keep outside generic generation |
| --- | --- |
| Clinical expiry, deceased-patient classification, orthodontic task/status construction, bit-mask clinical interpretation | Application/domain logic or explicitly authored mappings/predicates, never inferred conventions. |
| Declaration scanning, financial validation, tariff/NZA/insurer pricing | Handwritten services/adapters. Inheriting or delegating a real business implementation is not a generic persistence fake. |
| ZIP/file/blob output, image downloads, SOAP/insurance exchange formats | External adapters; test via scripted ports or handwritten adapter fakes. No built-in transport/file-format simulator. |
| Certificate generation, passwords, encryption, valid-from/to policies | Cryptographic/application fixtures, not repository-generation algorithms. |
| SignalR message routing and durable outbox delivery | Existing transport/application boundaries. Generic call recording can help, but does not implement delivery guarantees. |
| Settings defaults, XML/schema conversion, module flags and initialize-on-read rules | Consumer services/adapters. Scope-aware keyed storage is generic; the settings engine is not. |
| Target Application migration wrappers/internal entity access and its lookup seeds | Explicit consumer type adapters/fixtures. Do not reference Target Application assemblies or bake names/defaults into RepoGen. |

## 6. F15: optional generated test doubles

**Decision:** add F15 to the [overall roadmap](../future/REPOSITORY-FEATURE-PLAN.md). It is disabled by default and configured through RepositoryDesigner. No new test output, test dependencies or production registration changes should appear in a simple setup.

**Current delivery:** `UseScriptedTestDoubles(namespace)` and entity `GenerateTestDouble(bool?)`
emit separate fixtures for exact closed/bound ports. Typed queues/matchers/handlers, cancellation,
cold streams and bounded explicitly snapshotted history cover scripted F15.1/F15.5. In script-only
mode all operations remain RequiresOverride.
[Shared in-memory fixtures](../implemented/testing/INMEMORY-FIXTURES.md) now deliver F15.2: explicitly keyed entity/public
data-shape stores, detached seeds/inspection, duplicate policies, shared partitions and atomic
baseline capture/restore. [Portable fixture reads](../implemented/testing/INMEMORY-QUERIES.md) deliver the first F15.3
backend, strict receipts and automatic read overrides. Focused EF-free testing examples and shared
fake/SQLite read cases are delivered. The separate [contract-suite/provider harness](../implemented/testing/REPOSITORY-CONTRACT-FIXTURES.md)
adds reusable assertions and explicit real-provider setup/reset/scopes. Typed replacement/multi-source
roots, joins/subqueries/set operations now execute over atomic participating-store snapshots with
root visibility preserved. Explicit public scalar mappings now support suitable private/internal
entity reads, with complete per-operation member coverage and no persistence-model dependency.
[Declared navigation reads](../implemented/testing/INMEMORY-NAVIGATION.md) now load suitable public entity-shaped relationships
and unfiltered paths from actual EF keys, without inferring related visibility.
[Mapped public-shape navigation](../implemented/testing/INMEMORY-PROXY-NAVIGATION.md) combines explicit scalar maps and
declared edges for internal/read-only models, without constructing persistence entities.
[Explicit required many-to-many reads](../implemented/testing/INMEMORY-MANY-TO-MANY.md) additionally capture a declared
ordinary CLR join store with both endpoints, including public scalar maps and inverse DTOs.
Optional source links and explicitly non-null-filtered optional targets additionally use
[bounded nullable-link reads](../implemented/testing/INMEMORY-OPTIONAL-LINKS.md). Implicit/shared joins, unguarded target
null propagation/loading and provider-specific behavior retain overrides.
[Stateful fixture writes](../implemented/testing/INMEMORY-WRITES.md)
add designed tracked scalar Create/Patch/Delete and [bounded root-row batches](../implemented/testing/INMEMORY-BATCHES.md), original-token checks, pending/saved state, atomic shared
saves and checkpoint rollback. [Explicit scalar generated-value factories](../implemented/testing/INMEMORY-GENERATED-VALUES.md)
add seed-aware IDs/defaults and save-time computed tokens. [Public scalar write adapters](../implemented/testing/INMEMORY-PROXY-WRITES.md)
now support explicit public rows backed by internal models. [Explicit immediate scalar simulations](../implemented/testing/INMEMORY-IMMEDIATE-WRITES.md)
add Update/Delete/returning/consume without flushing pending writes or claiming SQL/race guarantees.
Suitable [saved-state insert/upsert](../implemented/testing/INMEMORY-ATOMIC-WRITES.md) and [bounded child-store reconciliation](../implemented/testing/INMEMORY-COLLECTIONS.md)
are now explicit per-command options.
[ReconcileLinks](../implemented/writes/LINK-RECONCILIATION.md) separately reuses the bounded child engine for ordinary
two-FK join rows, complete reference matching and payload writes without endpoint graph mutation.
[AllowSharedReferenceColumns](../implemented/writes/SHARED-REFERENCE-COLUMNS.md) additionally enables compatible owner/target
overlaps, validating all complete reference keys against the selected owner before collection loading
or mutation. It retains independent fixture opt-ins, immutable FKs and provider/authorization boundaries.
[Named target/additional reference roles](../implemented/writes/ADDITIONAL-LINK-REFERENCES.md) now additionally account
for required disjoint extra FKs, complete Add-only scalar maps and immutable matched references.
Referenced entities are never written or implicitly authorized.
[ReturnsSaved](../implemented/writes/SAVED-COMMAND-RESULTS.md) additionally reuses
declared ordinary navigation snapshots for fresh saved DTOs, including
[input-ordered bounded patch batches](../implemented/writes/SAVED-BATCH-RESULTS.md). Broader graph shapes and
additional value/provider policies remain extensions; arbitrary entity identity/behavior is not proxy-adapted,
and the harness does not complete the automatic backend.

[HasInMemoryTranslation](../implemented/testing/INMEMORY-TRANSLATIONS.md) now provides explicit scalar-call fixture semantics
for suitable provider/helper methods in named reads. The same designer governs root/result/group/terminal
and supported source expressions; production calls remain unchanged. The opt-in pattern helper deliberately
does not simulate database collation, SQL Server bracket syntax or Unicode/code-point guarantees.
[Per-command UseInMemoryTranslations](../implemented/testing/INMEMORY-COMMAND-TRANSLATIONS.md) independently reuses those
registrations in supported command selection/guards/setters/versions, bounded batches, child/link
payloads and saved scalar/navigation results. It is off per command and does not relax write
permissions or structural coverage. Store/copy/read-map/write-map/generated-value adapters remain
separate; no provider behavior is inferred.

[HasInMemoryValueEquality](../implemented/testing/INMEMORY-VALUE-EQUALITY.md) additionally acknowledges suitable CLR scalar
comparer metadata for tracked state/versions and non-forced field-aware merges. It is explicit and
store-scoped, not a query/key/provider comparer. Lossless copy/write maps and existing identity,
relationship, generated/binary-token and SQL/conversion boundaries stay intact.

### Separate two generated modes from real-provider verification

| Mode | Purpose | Boundary |
| --- | --- | --- |
| Scripted contract double | Return/fail/defer explicitly arranged operations, record calls, implement exact selected application ports. | No inference of persistence or business behavior. Unarranged calls fail clearly. |
| Stateful in-memory backend | Execute the validated portable subset of configured queries/writes against an isolated shared store. | Same contract, not universal SQL/EF equivalence. Unsupported operations require an explicit override or are rejected. |
| Optional relational fixture | Exercise real generated EF repositories with selected test provider and fixture setup. | Separate harness, not a pure in-memory fake; SQLite does not prove SQL Server-specific behavior. |

Start with **closed-operation contracts**. They already describe complete service-facing methods and hide EF. Compatibility selectors expose ORM/context behavior; a context-free substitute for every selector API must not be promised. Later selector support should be an explicit, separately validated capability.

### Configuration dimensions

Names below describe intended capabilities, not a finalized API.

| Dimension | Safe initial default | Opt-in configuration |
| --- | --- | --- |
| Generation | Off | Selected modules/entities/ports/operations; separate test namespace/output project |
| Backend | Explicit choice when enabled | Scripted, stateful portable backend, or separately configured relational harness |
| Store lifetime | Isolated fixture scope | Shared context-keyed state; explicit fixture singleton across request scopes |
| Data isolation | Detached seed/inspection/read snapshots for supported shapes | Registered copy adapter or explicitly selected live/reference semantics |
| Seeds | Empty | Consumer-owned seed factories, baseline restoration, duplicate/key policies |
| Writes | Designer-declared Stage/Save/outcomes | Portable simulated constraints/versioning, deterministic test generated values |
| Recording | Off | Typed counters/history/outcomes with snapshots, limits and independent reset |
| Unsupported operation | Strict diagnostic / unexpected-call failure | Explicit typed implementation override or scripted handler |
| Time and async behavior | No fabricated business timestamps/delays | Consumer clock/ID hooks and controllable async handlers |
| Production registration | Unchanged | Explicit test-only replacement/aliases in the fixture |

### Implementation slices and dependencies

| Slice | Priority | Deliverable | Feature-map coverage | Current status |
| --- | --- | --- | --- | --- |
| F15.1 | P1 | Optional test-output model, exact closed/bound/CQRS contracts, selected test helper contracts, scripted handler skeletons, per-operation coverage diagnostics. Reuse F02/F14 signatures/output ownership. | IM01, IM11, IM15 | Closed/bound contracts delivered; EF-facing selectors excluded |
| F15.2 | P1 | Shared context-keyed fixture store, deterministic keys/type adapters, seed/range/replace/reset/baseline/snapshot helpers, copy policy and same-instance DI aliases. Require inaccessible/internal data shapes to have explicit adapters. | IM02-IM05, IM14 | Delivered with explicit public data shapes, key/copy adapters, consumer-owned seeds and opt-in fixture-key comparers; no generated-value inference or query/provider collation changes |
| F15.3 | P1 | Portable query lowering from the same designer model: typed criteria/fragments, guards, visibility, membership, sort, projections, list/single/exists/scalar/dictionary/page/group shapes in a published capability matrix. Add joins/navigation shapes incrementally with shared sources. | IM06, IM07, IM15 | Single/multi-source reads, public scalar maps and declared entity-shaped/mapped navigation delivered: keyed references/collections, explicit CLR many-to-many joins, optional sources/guarded targets, nested DTOs, unfiltered paths, joins/subqueries/set operations, coordinated snapshots, pages/cursors/streams and context keys. Optional [final-model scalar filters](../implemented/testing/INMEMORY-MODEL-FILTERS.md) add row-only roots with independent visibility/bypasses. Optional [null-preserving target reads](../implemented/testing/INMEMORY-NULL-TARGETS.md) add explicit null-safe reporting without SQL propagation/loading simulation. Optional [relationship-key comparison](../implemented/testing/INMEMORY-RELATIONSHIP-COMPARERS.md) covers declared principal components without changing query/store/write equality. Optional [scalar-call translations](../implemented/testing/INMEMORY-TRANSLATIONS.md) add explicit fixture semantics. Unmapped/identity/untranslated provider/implicit/shared/unsafe-null expressions/explicit optional-target loading retain overrides |
| F15.4 | P1 | Typed portable create/patch/delete/range behavior, configured outcomes, pending/committed state and multi-repository rollback simulation. Add expected-state/version/consume simulations explicitly; fail on provider-only operations without an override. | IM08-IM10, IM13 | Tracked scalar/bounded batch writes, CreateMany, explicit versions/factories, public scalar maps, shared Stage/Save/checkpoint rollback, immediate scalar writes/insert/upsert, [child reconciliation](../implemented/testing/INMEMORY-COLLECTIONS.md), [composite matching](../implemented/writes/COMPOSITE-COLLECTION-KEYS.md), [explicit two-FK link reconciliation](../implemented/writes/LINK-RECONCILIATION.md) and [fresh saved DTO/navigation results](../implemented/writes/SAVED-COMMAND-RESULTS.md) delivered. [Per-command scalar-call translations](../implemented/testing/INMEMORY-COMMAND-TRANSLATIONS.md) add explicit fixture selection/value/result policies without production rewrites or relaxed permissions. [Tracked scalar equality policies](../implemented/testing/INMEMORY-VALUE-EQUALITY.md) additionally support ordinary non-identity values/tokens and field-aware merges with strict round trips. [Explicit shared-reference columns](../implemented/writes/SHARED-REFERENCE-COLUMNS.md) add complete-key owner guards while retaining immutable FKs. [Named additional references](../implemented/writes/ADDITIONAL-LINK-REFERENCES.md) add complete create-only FK maps with immutable matches. [Explicit optional additional references](../implemented/writes/OPTIONAL-ADDITIONAL-REFERENCES.md) add all-or-none nullable creation and immutable presence/values. Optional [per-command model filters](../implemented/testing/INMEMORY-MODEL-WRITE-FILTERS.md) cover scalar selection/fresh results without filtering inserts/shared Save or enabling native/graph behavior. Broader graph shapes and additional value/provider policies remain extensions |
| F15.5 | P1 | Typed argument-matched/queued results and failures, async delegates, cancellation, bounded snapshotted call history and test hooks. Keep scripted overrides visible in the coverage receipt. | IM11-IM15 | Scripted behavior and automatic read/write overrides delivered |
| F15.6 | P1 | Focused RepositoryDesigner examples and reusable contract suites; optional relational-fixture registration/setup; deterministic regeneration/API/coverage checks. | IM16 and regression coverage for all slices | EF-free named contract runner, explicit-provider DI fixture harness and forty-three testing examples delivered; further cases accompany remaining backend slices |

Dependency order: F15.1 first; F15.2 enables F15.3/F15.4; F15.5 extends the initial scripted mode and stateful overrides; F15.6 accompanies every delivered slice. This is not a request to implement all of Target Application's placeholders or services.

IM04's configurable fixture-key identity is delivered through [designer comparer factories](../implemented/testing/INMEMORY-KEY-COMPARERS.md),
including explicit composite keys. This does not infer SQL collation or rewrite criteria/relationship
equality. [MemoryKeyIdentity](../../samples/ConfigurationExamples/MemoryKeyIdentity/README.md) deliberately
demonstrates the difference between fixture key lookup and the unchanged named-query predicate.

Raw/temporal SQL, provider-specific bulk/list inputs, database-generated rowversions and other relational-only semantics are not silently evaluated as LINQ-to-Objects. Suitable native returning Update/Delete may explicitly opt into [scalar fixture simulation](../implemented/testing/INMEMORY-IMMEDIATE-WRITES.md); suitable InsertIfAbsent/InsertOrUpdate have their own [saved-state opt-in](../implemented/testing/INMEMORY-ATOMIC-WRITES.md). No SQL sequence, locking, constraint or race guarantees are inferred. Uncovered shapes retain RequiresOverride/Unsupported with real-provider guidance.

Optional [final-model scalar filters](../implemented/testing/INMEMORY-MODEL-FILTERS.md) extend F15.3/IM06 on explicitly
opted-in row-only roots. Each From dataset filters before composition; model-filter and designer
visibility bypasses remain independent. Captured contexts, filtered navigation graphs, AutoInclude
and unconfigured/unsupported filtered writes still require overrides. Production/API/cache shapes remain unchanged.

### Focused examples

Optional [ordinary filtered navigation reads](../implemented/testing/INMEMORY-FILTERED-NAVIGATION.md) extend F15.3/IM06-IM07.
Each filtered participant opts into pre-loading scalar filters independently; collections and locally
guarded optional references retain named pipelines, bypasses and detached snapshots. Required filtered
references, filtered many-to-many, unsafe expressions and graph writes remain explicit boundaries.

Optional [constant model defaults and scalar sentinels](../implemented/testing/INMEMORY-MODEL-DEFAULTS.md) extend F15.4/
IM04/IM08 without evaluating SQL or inventing identity/graph/provider behavior. Explicit factories
retain precedence; typed comparisons, mapped writes, Stage/shared Save and input-ordered batches
preserve supplied values. Seeding and updates do not apply insert defaults.

Optional [named-query model-loading control](../implemented/queries/EXPLICIT-LOADING-POLICIES.md) extends F08/F15.3/IM07:
explicit IgnoreAutoIncludes suppresses user-configured eager loading in production and removes only
that fixture boundary. Explicit relationships/profiles, query filters and visibility stay separate;
owned loading, unsupported graphs and command policy coverage are not implicitly enabled.

Optional [filtered saved DTO graphs](../implemented/testing/INMEMORY-SAVED-FILTERS.md) extend F15.4/IM08. Independent
command-root and related-dataset permission supports fresh ordinary collection/optional-reference
results after the shared Save, without graph mutation inference or silently shortened batches.
Required filtered references, filtered skip graphs, unsafe propagation and provider policies stay explicit.

Optional [model-filtered native result simulation](../implemented/testing/INMEMORY-NATIVE-FILTERS.md) extends F15.4/IM08
through the existing command simulation/filter opt-ins. Saved selection precedes changed/deleted
value return, not a fresh filtered result read; pending/version/rollback semantics remain.
Filtered atomic insert/upsert, graph/shadow results and provider guarantees stay separate.

These forty-three examples accompany delivered slices; remaining backend extensions should add similarly focused designers:

- **[MemoryNativeFilters](../../samples/ConfigurationExamples/MemoryNativeFilters/README.md) (delivered):** model-filtered native update/delete values, separate read/command opt-ins, no fresh result filtering, retained pending changes, token conflicts and explicit rollback/baselines.

- **[MemorySavedFilters](../../samples/ConfigurationExamples/MemorySavedFilters/README.md) (delivered):** separately configured filtered saved graphs, collection counts/optional targets, command-owned root policy, input-ordered batches, cross-repository shared Save and explicit rollback after post-save result failure.

- **[MemoryFilteredNavigation](../../samples/ConfigurationExamples/MemoryFilteredNavigation/README.md) (delivered):** explicit model-filter graph permission, collection counts/DTOs, locally guarded optional targets, independent visibility/ignore, From/pages/streams and unchanged seeds/snapshots.

- **[MemoryLoadingPolicies](../../samples/ConfigurationExamples/MemoryLoadingPolicies/README.md) (delivered):** one designer maps to exact contracts/production/fixture/manifest output, with per-query model-loading suppression, retained explicit profiles, public mapped DTOs, From roots, pages, cold streams and detached baseline isolation.
- **[MemoryModelDefaults](../../samples/ConfigurationExamples/MemoryModelDefaults/README.md) (delivered):** store-scoped constant defaults, typed sentinels, supplied zero/false values, public maps, Stage/shared Save, ordered batches and seed/update/baseline isolation.

- **[MemoryCriteria](../../samples/ConfigurationExamples/MemoryCriteria/README.md) (delivered):** one designer, optional text/price/ID criteria, named DTO mapping, sorting/pages, grouped totals, cold streams and automatically keyed fixture reads.
- **[MemoryJoins](../../samples/ConfigurationExamples/MemoryJoins/README.md) (delivered):** two entity-shaped stores, explicit composite/left joins, correlated anti-joins, union, grouped pages and cold streams, with root policies applied before composition.
- **[MemoryProxies](../../samples/ConfigurationExamples/MemoryProxies/README.md) (delivered):** internal EF model, distinct public arrangement/result types, explicit scalar read mappings, unmapped-field overrides and an EF/model-free consumer.
- **[MemoryNavigation](../../samples/ConfigurationExamples/MemoryNavigation/README.md) (delivered):** explicitly declared composite/reference/collection relationships, nested DTOs, unfiltered load profiles, root-versus-related visibility and cold coordinated snapshots; no pre-linked graph or EF consumer dependency.
- **[MemoryProxyNavigation](../../samples/ConfigurationExamples/MemoryProxyNavigation/README.md) (delivered):** internal persistence models with read-only/private-setter navigations, immutable public seeds, explicit scalar/relationship declarations and nested DTOs; one canonical mapped graph without persistence construction or EF/model consumer dependencies.
- **[MemoryManyToMany](../../samples/ConfigurationExamples/MemoryManyToMany/README.md) (delivered):** explicit required CLR join stores with public endpoint/join maps, complete composite keys, shared tags, accessed inverse edges, nested DTOs and explicit root/related visibility; no EF/model consumer dependency or graph-write inference.
- **[MemoryOptionalLinks](../../samples/ConfigurationExamples/MemoryOptionalLinks/README.md) (delivered):** direction-specific nullable source FKs and leading non-null target filters in shared designer projections/counts, inverse visibility, public scalar maps, detached snapshots and explicitly scripted unguarded SQL semantics; no EF/model consumer dependency or new global switch.
- **[MemoryNullTargets](../../samples/ConfigurationExamples/MemoryNullTargets/README.md) (delivered):** explicit per-navigation missing-target slots, null-safe counts/presence/DTO expressions, reused list/page/stream projection, detached snapshots and unchanged required-key/pair rules; unsafe dereferences, identity and explicit optional loading remain scripted.
- **[MemoryRelationshipComparers](../../samples/ConfigurationExamples/MemoryRelationshipComparers/README.md) (delivered):** explicit principal-key component factories shared by reference/collection/join directions and uniqueness checks; complete composite/null rules, detached streams and unchanged query/store/write equality, without EF/models in the consumer.
- **[MemoryLinks](../../samples/ConfigurationExamples/MemoryLinks/README.md) (delivered):** explicit two-FK join-row linking/payload update/removal, complete target matching, parent/link versions, shared Stage/Save, saved DTOs and rollback; no endpoint creation/deletion, inferred related authorization or fake SQL FK constraints.
- **[MemorySharedLinks](../../samples/ConfigurationExamples/MemorySharedLinks/README.md) (delivered):** explicit shared owner/target FK columns, complete owner-consistent reference keys, immutable ownership, public mapped application ports, saved DTOs and unchanged Stage/Save/rollback; no inferred tenancy, authorization, target mutation or fixture SQL constraints.
- **[MemoryAdditionalReferences](../../samples/ConfigurationExamples/MemoryAdditionalReferences/README.md) (delivered):** explicit matching target plus composite/scalar additional FK roles, complete Add-only maps, immutable matched references, public mapped CQRS ports and saved related DTOs; no endpoint writes, inferred authorization/workflows or fixture FK constraints.
- **[MemoryOptionalReferences](../../samples/ConfigurationExamples/MemoryOptionalReferences/README.md) (delivered):** explicitly optional additional FK roles, nullable composite absence/presence, once-only complete creation maps, partial-null rejection, immutable matched values/presence, public CQRS ports and Stage/Save/rollback; no optional matching target or inferred database constraints.
- **[MemoryTranslations](../../samples/ConfigurationExamples/MemoryTranslations/README.md) (delivered):** exact designer-declared provider/helper translations across named read clauses, mapped public rows, groups and streams; explicit ordinal/null/escape pattern behavior, unchanged production SQL/API/cache definitions and no universal provider-equivalence claim.
- **[MemoryWriteTranslations](../../samples/ConfigurationExamples/MemoryWriteTranslations/README.md) (delivered):** independent per-command scalar-call translations for provider selection, guarded CLR setters, shared Stage/Save/versions, fresh SQL-function DTO results and immediate-delete rollback; internal models/public seeds with unchanged production/protection rules and no EF/model consumer dependency.
- **[MemoryProjectionHelpers](../../samples/ConfigurationExamples/MemoryProjectionHelpers/README.md) (delivered):** explicit scalar-input flat DTO helper recipes, named mapping reuse, counted pages/cold streams and separately opted-in fresh saved DTOs; original production mapping and no inferred graph or SQL predicate translation, without EF/models in the consumer.
- **[MemoryScalarEquality](../../samples/ConfigurationExamples/MemoryScalarEquality/README.md) (delivered):** explicit nullable value/string-token equality for tracked state and versions, equivalent staged/saved assignments, unchanged literal query matching and strict public-map round trips, with no EF/model consumer dependency or inferred SQL/key comparison.
- **[MemoryProxyWrites](../../samples/ConfigurationExamples/MemoryProxyWrites/README.md) (delivered):** internal persistence models backed by immutable public rows, complete scalar read/write maps, explicit generated values, cross-repository Stage/Save, batches and rollback, without an EF/model consumer dependency or inferred reverse DTO mapping.
- **[MemoryKeyIdentity](../../samples/ConfigurationExamples/MemoryKeyIdentity/README.md) (delivered):** opt-in fixture-key comparers for duplicate policies, mapped pending/saved rows and resets, without rewriting queries, relationship matching, partitions or production collation.
- **[MemoryImmediateWrites](../../samples/ConfigurationExamples/MemoryImmediateWrites/README.md) (delivered):** per-command scalar Update/Delete/returning/consume simulation, original-row setters, counts/outcomes, saved-only targets, pending isolation and checkpoint rollback, without native SQL/race guarantees or EF/model consumer dependencies.
- **[MemoryAtomicWrites](../../samples/ConfigurationExamples/MemoryAtomicWrites/README.md) (delivered):** per-command insert/existing/conditional-upsert outcomes, composite conflict targets, update-only visibility/versions, explicit fixture IDs, retained staging and checkpoint rollback, without EF/model/provider dependencies or SQL sequence/locking/race guarantees.
- **[MemoryCollections](../../samples/ConfigurationExamples/MemoryCollections/README.md) (delivered):** per-command bounded leaf-child reconciliation in separate public fixture stores, complete ownership binding, add/update/missing policies, visible-only scope, parent/child versions, reserved IDs, shared Stage/Save and rollback, with no inferred graph persistence or SQL guarantees.
- **[CompositeCollections](../../samples/ConfigurationExamples/CompositeCollections/README.md) (delivered):** complete Code/Locale child matching within composite ownership, immutable ordered components, uniqueness and early null/duplicate guards, public rows, versions and Stage/Save/rollback; no provider collation or broader graph inference.
- **[MemorySavedResults](../../samples/ConfigurationExamples/MemorySavedResults/README.md) (delivered):** explicit fresh saved nested DTOs after scalar patch, shared save, child reconciliation and creation; declared public-map relationship snapshots, explicit related visibility and checkpoint rollback, with no atomic returning/provider equivalence claim.
- **[MemoryBatchResults](../../samples/ConfigurationExamples/MemoryBatchResults/README.md) (delivered):** bounded PatchMany with one fresh saved related DTO read, complete composite primary keys and input ordering; shared-save relationship snapshots, public maps, result failure and checkpoint rollback, without EF/model consumer dependencies or atomic-returning guarantees.
- **[MemoryCreateBatches](../../samples/ConfigurationExamples/MemoryCreateBatches/README.md) (delivered):** bounded CreateMany with all-candidate preparation, shared generated-value reservations, ordered fresh DTOs, Stage/shared Save and rollback; no artificial matching key, native bulk insertion or EF/model consumer dependency.
- **[MemorySharedWrites](../../samples/ConfigurationExamples/MemorySharedWrites/README.md) (delivered):** two repositories sharing one selected fixture unit of work, explicit Create/Patch/Delete and version setters, detached pending/saved rows, shared Stage/Save, checkpoint rollback and conflict/not-found scenarios; no generated-value or graph inference.
- **[MemoryBatches](../../samples/ConfigurationExamples/MemoryBatches/README.md) (delivered):** bounded PatchMany/leaf DeleteMany matched by a unique scalar other than the fixture primary key, all-target preparation, optional fields, per-item tokens, ordered saved values and Stage/Save/rollback; no generated-value or SQL constraint inference.
- **[MemoryGeneratedValues](../../samples/ConfigurationExamples/MemoryGeneratedValues/README.md) (delivered):** explicit seed-aware ID/default factories, save-time computed tokens, supplied defaults, generated batch DTOs and rollback replay; no SQL identity, trigger or rowversion equivalence claim.
- **[ScriptedPorts](../../samples/ConfigurationExamples/ScriptedPorts/README.md) (delivered):** overloaded ports, queued failure/success and frozen arguments. Deferred completion and cancellation also have runtime/generation tests.
- **[ScriptedStreams](../../samples/ConfigurationExamples/ScriptedStreams/README.md) (delivered):** one read-only designer stream, cold invocation, early disposal and cancellation, with an EF-free consumer.
- **[MemoryFixtures](../../samples/ConfigurationExamples/MemoryFixtures/README.md) (delivered):** two scoped shared stores, typed keys, detached nested copies and atomic all-store baseline reset; operations remain scripted.
- **[MemoryPartitions](../../samples/ConfigurationExamples/MemoryPartitions/README.md) (delivered):** public storage proxy, typed context partitions, per-key reset and explicit singleton data across scoped scripts.
- **[MemoryCoverage](../../samples/ConfigurationExamples/MemoryCoverage/README.md) (delivered):** strict acknowledged EF-filter/navigation operations, inspectable reasons and explicit typed read/write overrides; never a silent empty result.
- **[MemoryModelFilters](../../samples/ConfigurationExamples/MemoryModelFilters/README.md) (delivered):** optional final-model scalar filters on each row-only read root, independent visibility/bypass rules, filtered From joins, pages/cold streams and retained fixture seeds; captured contexts/navigation graphs/writes remain explicit overrides.
- **[MemoryModelWrites](../../samples/ConfigurationExamples/MemoryModelWrites/README.md) (delivered):** separately opted-in scalar command filters, original-token outcomes, saved-row staging, shared Save, immediate counts, unfiltered insertion and explicit rollback after fresh-result failure; no implicit write enablement from reads or inferred graph/native semantics.
- **[RelationalContractFixture](../../samples/ConfigurationExamples/RelationalContractFixture/README.md) (delivered):** one designer and one application-port contract suite on both generated fakes and real SQLite repositories, with explicit per-case seed/reset isolation.
- **[RelationalWrites](../../samples/ConfigurationExamples/RelationalWrites/README.md) (delivered):** actual generated IDs, versions, two staged repositories/shared save and transaction rollback verified from fresh scopes; not fake persistence simulation.

Each example must contain a clearly named RepositoryDesigner, its input context/model, checked-in generated contract/testing output, a small usage/test file and a README showing “this designer produces these files.” A runner-only Program.cs is not sufficient.

### Acceptance criteria

- With no testing configuration, existing output/dependencies/registrations are unchanged.
- Tests compile against the same EF-free application contracts, without requiring EF or production storage types in the service project. Any test-backend/model dependency is isolated in the testing output.
- Exact overloaded signatures, typed IDs, nullability, collections and result outcomes compile; one implementation satisfies the selected read/write/fixture aliases.
- Seed/reset/history isolation holds across tests and context keys; cloned snapshots cannot silently change stored data.
- Portable queries use the same designer definitions and request validation. Required visibility policies cannot vanish in the fake.
- Every selected operation has an inspectable Generated/RequiresOverride/Unsupported status. Missing handlers/unsupported shapes fail explicitly; no default-success placeholders.
- Writes preserve explicit Stage/Save, missing/duplicate/conflict semantics and shared-store rollback within the documented simulation.
- Cancellation and scripted sequences are deterministic; recorded mutable arguments are frozen according to the declared copy policy.
- Portable contract suites run against fake and real implementations. SQL translation, transactions/locking, rowversions and provider-specific commands remain covered by real-provider tests.
- Generic fixtures/examples contain no Target Application business names, lookup values, clinical rules or dependency on Target Application source.

## Conclusion

The optional [CreateMany slice](../implemented/writes/CREATE-MANY-COMMANDS.md) also covers F09/F15.4/IM08-IM09 bounded
scalar creation: all-candidate preparation, Stage/shared Save, row-derived ID reservations and
input-ordered saved DTOs on both fixture shapes. The
[MemoryCreateBatches designer](../../samples/ConfigurationExamples/MemoryCreateBatches/README.md)
adds the twenty-fifth testing example. Native bulk import, implicit graph insertion and SQL constraint
equivalence are not inferred.

The [nullable-link slice](../implemented/testing/INMEMORY-OPTIONAL-LINKS.md) additionally covers F15.3/IM06-IM07 optional
source FKs and explicitly non-null-filtered target projections/counts, including saved DTOs.
[MemoryOptionalLinks](../../samples/ConfigurationExamples/MemoryOptionalLinks/README.md) is the twenty-sixth
testing example. Unguarded SQL null propagation/loading retains an explicit override rather than
silently changing the designed result.

The recurring need is not a universal `IInMemoryMockRepositories` abstraction. It is **optional generated implementations of the already designed ports, plus a deliberate test fixture surface**.

Generate the generic arrangement/query/write/recording machinery; retain domain/external adapters and explicitly unsupported behavior. This makes service tests simpler without making a fake look more complete than it is.
