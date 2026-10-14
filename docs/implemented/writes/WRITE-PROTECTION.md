# Optional designer write protection

`entity.ProtectWrite(property)` declares that a mapped scalar needs explicit `AllowWrite` permission
when a named command assigns it. This fills the gap between EF's key/generated/concurrency metadata
and ordinary application-owned audit/ownership scalars. Names such as `CreatedBy` or `OwnerId` do not
automatically imply a policy.

See the focused [WriteSafety designer example](../../../samples/ConfigurationExamples/WriteSafety/README.md)
for the exact declaration-to-output mapping and executable consumer.

## Configuration

```csharp
modelBuilder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Application.Generated"));
modelBuilder.Entity<Note>(entity =>
{
    entity.ProtectWrite(note => note.CreatedBy);

    entity.HasCommand("Create")
        .Create<CreateNoteRequest>(request => new Note { Title = request.Title, CreatedBy = request.Actor })
        .AllowWrite(note => note.CreatedBy)
        .Save();

    entity.HasCommand("Rename")
        .Where<RenameRequest>((note, request) => note.Id == request.Id)
        .Patch(setters => setters.SetProperty<string, RenameRequest>(note => note.Title, (note, request) => request.Title))
        .Save();
});
```

If a later change adds `CreatedBy` to the `Rename` setters, generation fails before publishing files.
Its explicit permission must appear on `Rename` itself. A permission on `Create` never applies to
another command, and runtime callers cannot enable one.

| Configuration | Default/effect |
| --- | --- |
| No `ProtectWrite` | Existing behavior and generated output remain unchanged |
| `ProtectWrite(property)` | Named assignments require local `AllowWrite` |
| Repeating the same declaration | Idempotent |
| `ProtectWrite(property, enabled: false)` | Removes this entity's explicit declaration |
| `command.AllowWrite(property)` | Permission for that command only; existing EF safety rules still apply |
| `children.AllowWrite(property)` | Permission for that collection mapping only, independent of the parent command |

Policy fields and permission receipts appear in the optional manifest. No runtime dependency,
permission parameter, mapper service, CLI option or EF-facing public type is added. Public contract/API
snapshots stay the same for a policy-only change that leaves operation signatures unchanged; a contract
diff is not a behavioral/write-policy compatibility guarantee. Review the designer and manifest too.

## Assignment paths

The guard applies to direct mapped scalar assignments in:

- Tracked `Create` and `Patch`, saved or staged.
- Named immediate `Update`, including `ReturnsMany` native writers.
- Atomic insert-if-absent/insert-or-update creation **and** update mappings.
- Explicit child `Add` and `Update` reconciliation mappings, even if no child repository is generated.

Set-based commands under an active policy also validate direct mapped setters using existing EF
key/generated/permission protections. Unsupported computed/navigation setter targets cannot hide a
protected scalar. This does not parse arbitrary method bodies, infer assignments inside constructors
or intercept direct EF code. Expression helpers and runtime setter/interceptor behavior remain
application responsibilities; the guard validates the generator's supported explicit mappings.

Closed repositories are required. CQRS selector and compatibility modes expose entities or generic
write selectors, so a named-mapping guard would not close their public write surface. Selecting an
unsupported surface fails rather than pretending those bypasses are protected.

## Child and inheritance boundaries

Declare child policy on `modelBuilder.Entity<Child>()`; it remains effective when
`child.RepositoryEnabled = false`. The parent selects and supplies ownership through the relationship,
but a parent `AllowWrite` does not grant child permission. A child mapping must declare its own
`AllowWrite`, including for an explicitly protected supplied matching key on creation.

Automatic FK assignment from the selected parent is not a request mapping. Child FKs and matching
keys still cannot be changed by child updates; adding `AllowWrite` does not enable reparenting.
Store-generated/immutable restrictions also remain in force. Expected-version selectors only read
original values; a protected version requires permission when a setter actually assigns it.

Policies are per configured EF entity, including inherited mapped scalars. A base entity's declaration
is not automatically applied to another entity's repository/command scope: configure each selected
derived entity explicitly or reuse an entity configuration module. No type-wide or name-based
convention is silently inferred.

## What this does not promise

This is a design-time mapping guard, not authorization, row-level security or a live database trigger.
Services still authorize callers and supply trustworthy requests; ownership belongs in their designed
selection predicates. EF interceptors, save participants, direct context writes, database defaults,
triggers and separately handwritten adapters can change fields outside these generated assignments.

Visibility, expected-version outcomes, shared saves, stage/transaction lifetimes and cache invalidation
retain their existing semantics. A guard does not hide fields from queries or affect `Exclude`
accessor selection. It does not generate an audit log or a domain policy engine.

## Diagnostics

| ID | Meaning |
| --- | --- |
| `RGD2901` | Explicit write protection requires closed repositories |
| `RGD2902` | Guard selector is not a direct mapped scalar (including ignored properties/navigations) |
| `RGD2903` | A named command/child mapping assigns a protected scalar without local permission |

Invalid configurations fail before generated-file publication. Other existing immutable/generated
write and closed-contract diagnostics still apply; `AllowWrite` cannot suppress them.
