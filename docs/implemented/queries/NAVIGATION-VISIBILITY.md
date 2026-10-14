# Optional navigation visibility review

RepoGen's `HasVisibilityPolicy(...)` filters an entity's repository/query **roots**. It does not
automatically filter references or collections reached through another entity's navigation.
`DependsOn<T>()` and inferred cache tags also do not enforce visibility.

The optional design-time guard makes that distinction reviewable:

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults()
    .UseClosedRepositories("Application.Contracts")
    .RequireNavigationVisibilityReview());

modelBuilder.Entity<Comment>(entity =>
{
    entity.RepositoryEnabled = false;
    entity.HasVisibilityPolicy("Public", comment => comment.IsPublic);
});

modelBuilder.Entity<Document>(entity =>
{
    entity.HasQuery("GetPublicDetails")
        .Load(load => load.Include(document => document.Comments
            .Where(comment => comment.IsPublic).OrderBy(comment => comment.Id)))
        .ReviewNavigationVisibility<Comment>("Public",
            "This load explicitly selects only public comments.")
        .ProjectTo(document => DocumentMapping.Details(document))
        .List();
});
```

Generation fails with `RGD2001` when an inspected navigation reaches a target with a configured
RepoGen policy but no matching review receipt. Configure actual protection first: an explicit
relationship predicate/projection, a filtered load profile, an appropriate EF global filter,
or a typed `From` join whose roots already receive their policies. A trusted historical operation
may deliberately have different visibility; document that reason in its receipt.

A receipt does **not** inject a filter, prove the reason correct, or authorize a caller. In
particular, `IgnoreQueryFilters()` still disables EF global filters. Receipts are reviewed designer
declarations, not a substitute for authorization or provider/SQL tests.

## Defaults and scopes

The guard is off in both compatibility and simple presets. Enabling it adds no service methods,
constructor dependency, runtime flag, or provider-specific package. It works with compatibility
selectors, CQRS selectors, and closed operations.

- Options `RequireNavigationVisibilityReview(bool)` supplies the global default.
- Entity, projection, query, command, and current result builders accept
  `RequireNavigationVisibilityReview(bool? enabled = true)`; `null` restores inheritance.
- Receipts use `ReviewNavigationVisibility<TTarget>(policyName, reason)` on those designer builders.
  Entity receipts cover that entity's configured operations; projection receipts are reusable by
  queries referencing that projection. Query/command receipts are narrower.
- Effective requirement order is query, referenced projection, entity, global; commands use
  command, entity, global. Receipt precedence is query/command, projection, entity. A local receipt
  can document a historical query differently from its ordinary shared projection.
- Receipts identify a target CLR entity **and one case-sensitive policy name**. Review each policy
  separately. A receipt covers all occurrences of that target/policy in its configuration scope,
  not just one path; choose operation-scoped receipts when paths have different semantics.
- A mapped target and declared policy are required (`RGD2002`), even when the guard is off.
  Reasons cannot be blank. Duplicate target/policy receipts in one scope fail (`RGD2003`);
  a receipt in a more specific scope is an intentional override.

Presets do not erase an explicitly configured guard. A deliberate `false` override is allowed and
appears as an unguarded operation in the audit inventory. This is configuration validation, not
a locked-down security mode.

## Inspected expressions and limitations

The tool uses the final EF model and expanded type-adapter expressions. It inspects ordinary
reference, collection, owned, inherited, and skip-navigation member access, plus constant-name
`EF.Property<T>(entity, "Navigation")`. Clauses include:

- Root/conditional/membership filters, sorting, named projections, grouping and result stages.
- Dictionary/lookup/scalar/cursor terminals, `From` factories and applied source visibility policies.
- Attached entity/projection/query loading, filtered includes and every `ThenInclude` continuation.
- Named command selection, setters, expected versions, and saved-result mappings.

Unused load profiles do not issue a query and are not separately guarded. Unused standalone
projections in closed mode and disabled operations/capabilities are not treated as exposed
operations. Compatibility/CQRS standalone projection/filter methods and selector defaults are
inspected because consumers can call them.

Direct typed sets are roots, not navigation access: normal joins/anti-joins need no navigation
receipt unless their expressions also traverse a navigation. Root `BypassVisibility` declarations
do not serve as navigation receipts. An excluded target repository can still have policies and
therefore still require review when accessed by another repository.

Opaque mapper/scalar methods, registered infrastructure providers, dynamic navigation names,
application authorization, and consumer-created runtime expressions are not analyzed internally.
Explicit loading remains inspectable even when the graph-to-DTO mapper is opaque. Registered
providers own their root policies and dependency declarations. This guard is not a complete data
flow/security analyzer; closed operation mode remains the way to remove consumer query escape APIs.

Required relationships can cause an EF global filter to remove the parent row; optional references
may become null. Filtered tracked loading can be affected by EF change-tracker fixup. Prefer the
default detached closed reads when the result must contain only the loaded graph; verify the
chosen tracking/relationship/provider semantics. The guard does not override EF behavior.

## Manifest audit

When explicitly configured, the manifest adds `navigationVisibility` containing the default,
operation overload identity, effective requirement, and each protected mapped edge with its target,
policy, reason and receipt scope. An unguarded edge can have a null reason. Ordering is stable;
overloads are distinct. Unconfigured generation has no new manifest section. Review receipts do
not change the generated query or public API snapshot.

## Dependency and cancellation hardening

Cached query dependencies now include navigation access in predicates, conditions, sorting,
dictionary/scalar/cursor terminals and source root policies, in addition to projections/loading.
Constant-name `EF.Property` navigations participate. Inspectable EF global filters are followed
transitively through navigations, typed `Set`/`DbSet` references and normalized EF entity roots,
including inherited filters. No captured getters or designer factories are invoked. Explicit
`IgnoreQueryFilters()` omits those filter-only dependencies; opaque registered roots continue to
require their own `DependsOn<T>()` declarations. Undiscoverable helper/query-filter behavior still
requires explicit dependencies. Inference is conservative, so pruned includes can cause extra
invalidation rather than stale results.

These dependency corrections are automatic correctness fixes for already configured caches;
they do not enable caching. Named and selector streams now check cancellation at each yield and
completion boundary, including SQL Server split-query buffering. Both code-first and attribute
generation retain this behavior. A canceled enumeration releases its owned lease, and an already
returned DTO remains usable without lazy loading.

## Runnable example and provider verification

The [Visibility designer](../../../samples/ConfigurationExamples/Visibility/Configuration/VisibilityRepositoryDesigner.cs)
uses a simple borrowed context, an EF global filter for optional owners, explicitly filtered public
comments, ordinary shared projections, a reviewed historical operation, and a policy-aware anti-join.
The [application](../../../samples/ConfigurationExamples/Visibility/Program.cs) calls only its handwritten
EF-free `IReviewedLibrary` port. No cache, resolver, leases, tenant or unit-of-work setup is required.

Run all twelve examples with:

```powershell
./samples/ConfigurationExamples/Generate.ps1 -Configuration Release -Run
```

Opt-in `SqlServerQueryCoverageTests` execute composite/left joins, anti/existence queries, set
operators, joined grouping/HAVING/pages, dictionary pruning, filtered nested single/split loading,
global-filter/bypass behavior, projection include pruning and owned stream cleanup/cancellation.
They also verify an untranslatable predicate fails without client filtering. Fixtures create/drop
only GUID-named tables in the explicitly supplied database; they never call database-level
`EnsureCreated`/`EnsureDeleted`. This proves those fixtures, not universal provider conformance.
