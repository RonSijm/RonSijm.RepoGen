# Designer validation diagnostics

Common configuration failures have stable `RGD` identifiers at the start of the exception/CLI
message. They validate the configuration before generated-file writes, not live database behavior.
Existing exception categories and explanatory text are retained. Normal/simple generation does
not need a diagnostic service or any new configuration.

The code-first runner validates by calling the regular in-memory engine:

```csharp
var bundle = RepositoryGenerator.Generate(context, designer);
// No generated files have been written. Choose Check(...) or Write(...) explicitly afterward.
```

The CLI's `--validate` also renders/validates without repository output writes. Building/loading
the model can still run application design-time factory code; use independently buildable inputs
and `--no-build` when appropriate. Errors are fail-fast, not an exhaustive aggregated list.

| ID | Configuration problem |
| --- | --- |
| RGD1001 | Query references a missing named/type projection |
| RGD1002 | Projection type/name reference is ambiguous |
| RGD1003 | Duplicate load-profile name, including across modules |
| RGD1004 | Duplicate closed operation CLR signature; return/nullability cannot distinguish overloads |
| RGD1005 | Invalid closed-operation identifier |
| RGD1101 | Port bound to a disabled read/write capability |
| RGD1102 | Invalid port shape; must be a public closed method-only interface with supported instance signatures |
| RGD1103 | Required port member has no configured operation |
| RGD1104 | Port input/return signature or nullable annotations do not match the operation |
| RGD1105 | Conflicting inherited port declarations, including parameter names/defaults |
| RGD1201 | Unsafe/incompatible context lifetime or resolver/closed-mode combination |
| RGD1202 | Missing/incompatible keyed-context declaration |
| RGD1203 | Closed caching missing explicit scope-aware configuration |
| RGD1301 | Invalid atomic writer capability configuration |
| RGD1302 | Registered query/atomic/returning writer requires a different EF provider |
| RGD1303 | Registered query root has an invalid context/request/result capability or mode |
| RGD1304 | Registered query root policies mixed with ordinary root policies |
| RGD1305 | Invalid/missing returning writer capability or non-closed mode |
| RGD1306 | Unsupported returning command, table shape, scalar assignment or result mapping |
| RGD1401 | Invalid include/navigation configuration for the final EF model |
| RGD1402 | Conflicting filtered includes for one navigation path |
| RGD1403 | Multiple collection paths require an explicitly configured single/split strategy |
| RGD1404 | Missing load-profile reference or profile from a different designer/entity |
| RGD1501 | Paging requires ordering after root/group/distinct replacement |
| RGD1502 | Anonymous/non-public/ORM/query/ref-only type escapes a closed application contract, including public bases/interfaces/members/helpers/constraints; message includes the path |
| RGD1601 | CQRS-selector surface combined with closed output or a generic context |
| RGD1602 | CQRS-selector surface requires regenerated tool-owned selectors, not attribute/reused types |
| RGD1603 | Configured projection name collides with a read selector member |
| RGD1604 | Active named writes configured on a keyless CQRS-selector entity |
| RGD1605 | CQRS generated type collides with another entity/generated type in its namespace |
| RGD1701 | Type-adapter marker called at runtime, or adapter instance cannot be discovered without invoking a getter/factory |
| RGD1702 | Adapter belongs to a different RepositoryModelBuilder |
| RGD1703 | Recursive adapter mapping definition |
| RGD1704 | Property/input adapter mismatch, duplicate mapping, absent/mismatched EF converter or unsupported accessor conversion |
| RGD1705 | Duplicate accessor/filter CLR overload signature, ignoring reference annotations |
| RGD1801 | Invalid/reserved generated persistence namespace |
| RGD1802 | Isolated namespace would reuse existing selectors rather than generate its own infrastructure |
| RGD1901 | Unsupported anonymous/open/delegate membership key or statically queryable/string value input |
| RGD1902 | Membership attached to a superseded entity/root builder rather than the current result builder |
| RGD1903 | Enabled caching would re-enumerate an unmaterialized membership input |
| RGD2001 | Required navigation visibility review is missing for a reached target policy |
| RGD2002 | Review references an unknown mapped target or case-sensitive visibility policy |
| RGD2003 | Duplicate target/policy navigation review in one designer scope |
| RGD2101 | Command-side query placement without a CQRS/closed public surface |
| RGD2102 | Active command-side query on a keyless entity without a write port |
| RGD2103 | Enabled caching on a command-side query |
| RGD2104 | Command-side query signature collides with a generated write/save helper |
| RGD2201 | Duplicate model-level fragment name for the same row and request types |
| RGD2202 | Invalid explicit fragment request parameter name |
| RGD2301 | Non-positive explicit entity/query/result/command SQL timeout |
| RGD2302 | Timeout override without closed operations owning the complete query/stream scope |
| RGD2401 | Conditional ThenBy chain without adjacent result ordering guaranteed on every branch |
| RGD2501 | Unknown dictionary duplicate-key policy |
| RGD2502 | First/last dictionary policy without ordering guaranteed on every branch |
| RGD2601 | Empty result sort-field whitelist, blank alias or duplicate case-insensitive alias |
| RGD2602 | Default result sort field absent from its whitelist, blank default or conflicting unspecified-field policies |
| RGD2701 | Request-aware entity cursor attached after result/root replacement instead of to the final result builder |
| RGD2702 | Cursor guard without a final cursor terminal, or guard cursor type does not match that terminal |
| RGD2801 | Unsupported collection reconciliation operation/navigation/key/limit or parent-version configuration; composite matching requires two-to-seven distinct direct non-null scalar components and proven unfiltered uniqueness |
| RGD2802 | Invalid child create/update/version reconciliation mapping; Add must supply every matching component and Update cannot change any of them |
| RGD2901 | Explicit scalar-write protection requires closed repositories |
| RGD2902 | Protected-write selector is not a direct mapped scalar |
| RGD2903 | Named command/child mapping assigns an explicitly protected scalar without local AllowWrite permission |
| RGD3001 | Unsupported or mixed batch command/surface/key/limit, duplicate batch declaration or cascading graph deletion |
| RGD3002 | Invalid/repeated batch scalar assignment, expected-version binding or navigation mapping/result |
| RGD3101 | Scripted test output without closed contracts or an explicit testing namespace |
| RGD3102 | Unsupported scripted contract shape or test fixture member/control collision |
| RGD3201 | Shared test stores without closed/scripted prerequisites or selected stores; inaccessible row/key types or invalid key/copy expressions |
| RGD3202 | Application port method collides with the shared Fixture property |
| RGD3203 | MapInMemoryEntity without a matching distinct public store, invalid initializer/scalar member, an inaccessible/non-portable assignment or a reserved read-row member collision |
| RGD3204 | HasInMemoryKeyComparer without an exact matching store key, or a null/captured/ambient/private/model/EF comparer factory, inaccessible constructor or object-cast escape |
| RGD3301 | Automatic fixture reads without store prerequisites, or strict read checking without automatic reads |
| RGD3302 | Non-portable read under RequirePortableTestQueries without a RequireTestOverride acknowledgement; includes absent mapped keys/scalars, undeclared/unselected endpoint/join stores, incompatible entity-shaped/mapped targets, implicit/shared joins and optional targets without a leading Where(target => target != null) on every use or with explicit loading |
| RGD3303 | Application operation collides with a reserved automatic fixture execution helper |
| RGD3304 | HasInMemoryNavigation selector is not a direct actual EF navigation member |
| RGD3401 | Automatic fixture commands without stores, or strict command checking without automatic commands |
| RGD3402 | Uncovered command under RequirePortableTestCommands without RequireTestOverride; immediate scalar Update/Delete/InsertIfAbsent/InsertOrUpdate need explicit SimulateImmediateWrites, which cannot bypass graph/cascade/provider or ordinary production boundaries; insert factories must actually have insert timing |
| RGD3501 | Invalid explicit in-memory generated-value adapter: store/row shape, duplicate or unmapped property, generation metadata, setter, sentinel, capture or unsupported expression |
| RGD3601 | Invalid MapInMemoryWrites: missing/mismatched public store/read map, incomplete ordinary scalar coverage, transformed/reordered primary-key aliases or non-portable write expression |
| RGD3701 | Invalid ReturnsSaved lifecycle: requires tracked Create/Patch or bounded CreateMany/PatchMany + explicit Save + readable complete CLR primary key; excludes Stage/Delete/immediate writes and redundant ReloadAfterSave |
| RGD3801 | Invalid CreateMany combination: closed ordinary scalar creation, finite positive bound and explicit Save/Stage required; existing-row predicates/setters/versions, mixed mappings, graphs and native/reload result modes excluded |
| RGD3802 | CreateMany mapping is not a non-empty parameterless entity initializer containing only mapped scalar assignments |
| RGD3901 | Invalid unconfigured ReconcileLinks relationship/reference key: two ordinary required owner/target FKs and complete metadata-ordered target matching required; extra FK roles need explicit named declarations |
| RGD3902 | Owner/target FK overlap lacks explicit shared-column permission, uses incompatible CLR keys, or leaves no independent target component |
| RGD4001 | Duplicate in-memory translation name/closed overload or invalid scalar call/direct-input/fixed-slot shape |
| RGD4002 | In-memory translation replacement captures state, constructs graphs, uses nonportable behavior or drops an input |
| RGD4003 | Recursive/cyclic in-memory translation expansion; declare an acyclic replacement graph |
| RGD4201 | Invalid named target/additional link-reference role: missing/duplicate/unaccounted roles, required/optional cardinality mismatch, optional matching target, non-direct/unmapped/owner selection, extra-column overlap or unsupported entity/relationship shape |
| RGD4202 | Link Add omits a scalar component of a required or optional additional reference; explicit null still needs an assignment |
| RGD4301 | Optional additional FK cannot represent complete absence: every component must be model/CLR-nullable and non-generated |
| RGD4401 | Missing-target preservation is configured on an ordinary navigation or a required many-to-many target; only an explicitly mapped optional-target skip navigation accepts this fixture policy |
| RGD4501 | Missing store, duplicate/non-principal/nullable/array/type-mismatched relationship component, or captured/ambient/nonportable comparer factory; declarations configure principal-key scalars for fixture-read relationships only |
| RGD4601 | Projection translation result is not an independent public flat scalar DTO, or final-model entity exclusion cannot be performed |
| RGD4602 | DTO translation captures/drops inputs, invokes nonportable behavior or uses private/non-scalar/graph constructors or bindings instead of explicit flat DTO construction |

These identifiers cover common composition, port, lifetime, provider, loading and paging boundaries,
not every possible argument check or unsupported expression/provider translation. Collection reconciliation
uses the distinct `RGD2801`/`RGD2802` family; `RGD2401` remains the conditional-ordering diagnostic.
LINQ translation
still belongs to EF and needs provider execution tests. A paging-order check cannot prove a sort is
unique; the designer must configure the tie-breaker.

Other diagnostic families remain separate: attribute-generator `RG001`–`RG005`, explicit contract
nullability `RGN`, generated output/freshness/ownership `RGG`, and API/data compatibility `RGA`.
See [type adapters](../repositories/TYPE-ADAPTERS.md), [contract nullability](../repositories/CONTRACT-NULLABILITY.md), [code-first generation](CODE-FIRST-GENERATION.md)
and [API compatibility](API-COMPATIBILITY.md). No error silently selects a fallback provider,
turns a write into a read, removes a required visibility policy, or accepts an API baseline.

Optional [compiler validation](COMPILATION-VALIDATION.md) uses a separate `RGV1001`–`RGV1004` family
for reference/configuration/gate failures, preserving ordinary `CS...` IDs for compiler diagnostics.
It is not enabled by normal generation and does not replace provider execution tests.
