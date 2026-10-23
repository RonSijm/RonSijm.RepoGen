using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CollectionReconciliationTests
{
    [Fact]
    public async Task SavedReplacementMapsOnlyApprovedFieldsAndNeverMovesAnotherOwnersChildren()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(Model(fixture.Context));
        var result = await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request(new("A", "Changed"), new("C", "New")));
        result.Status.Should().Be(CommandStatus.Applied);
        result.Value!.Keys.Should().BeEquivalentTo("A", "C");
        result.Value.Version.Should().Be(2);
        var rows = await fixture.Context.Children.AsNoTracking().OrderBy(child => child.Id).ToListAsync();
        rows.Where(child => child.ParentId == 1).Select(child => child.Key).Should().BeEquivalentTo("A", "C");
        rows.Single(child => child.ParentId == 1 && child.Key == "A").ProtectedNote.Should().Be("Keep this");
        rows.Single(child => child.ParentId == 1 && child.Key == "C").Id.Should().BeGreaterThan(3);
        rows.Single(child => child.ParentId == 2).Text.Should().Be("Other owner");
        fixture.Context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
    }

    [Fact]
    public async Task MissingItemsAreKeptByDefaultAndNewKeysRequireAnAddMapping()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(Model(fixture.Context));
        (await Invoke<CommandResult>(operations, "MergeAsync", Request(new CollectionItem("A", "Merged")))).Succeeded.Should().BeTrue();
        (await fixture.Context.Children.CountAsync(child => child.ParentId == 1)).Should().Be(2);
        Func<Task> unknown = async () => await Invoke<CommandResult>(operations, "MergeAsync", Request(new CollectionItem("Unknown", "Not allowed")) with { ExpectedVersion = 2 });
        await unknown.Should().ThrowAsync<ArgumentException>().WithMessage("*new or unknown keys*");
        (await fixture.Context.Children.SingleAsync(child => child.ParentId == 2)).Text.Should().Be("Other owner");
    }

    [Fact]
    public async Task ExplicitRejectMissingDoesNotMutateAnyRows()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(Model(fixture.Context, missing: RepositoryCollectionRemovalBehavior.Reject));
        Func<Task> invalid = async () => await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request(new CollectionItem("A", "Rejected")));
        await invalid.Should().ThrowAsync<ArgumentException>().WithMessage("*missing existing keys*");
        (await fixture.Context.Parents.SingleAsync(parent => parent.Id == 1)).Version.Should().Be(1);
        (await fixture.Context.Children.SingleAsync(child => child.Id == 1)).Text.Should().Be("Initial");
    }

    [Fact]
    public async Task AnOmittedOptionalCollectionDoesNotLoadChildrenAndNullIsNotAnEmptyReplacement()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(Model(fixture.Context));
        (await Invoke<CommandResult>(operations, "OptionalAsync", new OptionalCollectionRequest(1, default))).Status.Should().Be(CommandStatus.NoChanges);
        fixture.Context.ChangeTracker.Entries<CollectionChild>().Should().BeEmpty();
        Func<Task> nullInput = async () => await Invoke<CommandResult>(operations, "OptionalAsync", new OptionalCollectionRequest(1, new Optional<IReadOnlyList<CollectionItem>?>(null)));
        await nullInput.Should().ThrowAsync<ArgumentNullException>();
        (await Invoke<CommandResult>(operations, "OptionalAsync", new OptionalCollectionRequest(1, new Optional<IReadOnlyList<CollectionItem>?>([])))).Succeeded.Should().BeTrue();
        (await fixture.Context.Children.CountAsync(child => child.ParentId == 1)).Should().Be(0);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("nullKey")]
    [InlineData("nullItem")]
    [InlineData("tooMany")]
    [InlineData("nullList")]
    public async Task InvalidInputsFailBeforeAnOwnedContextIsAcquired(string scenario)
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = new OwnedProvider(fixture.Connection);
        var operations = fixture.Operations(Model(fixture.Context, leases: true, maximum: 2), provider);
        var request = scenario switch
        {
            "duplicate" => Request(new("A", "One"), new("A", "Two")),
            "nullKey" => Request(new CollectionItem(null!, "Invalid")),
            "nullItem" => Request((CollectionItem)null!),
            "tooMany" => Request(new("A", "One"), new("B", "Two"), new("C", "Three")),
            _ => Request() with { Items = null! }
        };
        Func<Task> invalid = async () => await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", request);
        await invalid.Should().ThrowAsync<ArgumentException>();
        provider.Acquisitions.Should().Be(0);
    }

    [Fact]
    public async Task OversizedExistingCollectionsFailWithoutPartialDeletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(Model(fixture.Context, maximum: 1));
        Func<Task> invalid = async () => await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request());
        await invalid.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exceeds its limit*");
        (await fixture.Context.Children.CountAsync()).Should().Be(3);
        fixture.Context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
    }

    [Fact]
    public async Task RootAndChildStaleVersionsReturnConflictAndLeaveTheGraphUnchanged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(Model(fixture.Context));
        (await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request(new CollectionItem("A", "Stale")) with { ExpectedVersion = 0 })).Status.Should().Be(CommandStatus.Conflict);
        fixture.Context.ChangeTracker.Entries<CollectionChild>().Should().BeEmpty();
        (await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request(new CollectionItem("A", "Stale", ExpectedVersion: 0)))).Status.Should().Be(CommandStatus.Conflict);
        (await fixture.Context.Parents.SingleAsync(parent => parent.Id == 1)).Version.Should().Be(1);
        (await fixture.Context.Children.SingleAsync(child => child.Id == 1)).Text.Should().Be("Initial");
        fixture.Context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
    }

    [Fact]
    public async Task AChildRaceDuringSaveRestoresParentChildrenAndNewEntityStates()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = new CollectionContext(new DbContextOptionsBuilder<CollectionContext>().UseSqlite(fixture.Connection).AddInterceptors(new ChildRace()).Options);
        var operations = fixture.Operations(Model(context), context: context);
        (await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request(new("A", "Losing"), new("C", "New")))).Status.Should().Be(CommandStatus.Conflict);
        context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        context.ChangeTracker.Entries<CollectionChild>().Select(entry => entry.Entity.Key).Should().BeEquivalentTo("A", "B");
        var parent = await context.Parents.Include(parent => parent.Children).SingleAsync(parent => parent.Id == 1);
        parent.Children.Select(child => child.Key).Should().BeEquivalentTo("A", "B");
        parent.Version.Should().Be(1);
        (await context.Children.AsNoTracking().SingleAsync(child => child.Id == 1)).Version.Should().Be(2);
    }

    [Fact]
    public async Task AConstraintFailureRestoresOnlyThisCommandsChangesAndPreservesUnrelatedPendingEdits()
    {
        await using var fixture = await Fixture.CreateAsync();
        var unrelated = await fixture.Context.Parents.SingleAsync(parent => parent.Id == 2);
        unrelated.Name = "Pending unrelated edit";
        var operations = fixture.Operations(Model(fixture.Context));
        Func<Task> failure = async () => await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request(new("A", "Shared"), new("C", "Shared")));
        await failure.Should().ThrowAsync<DbUpdateException>();
        fixture.Context.Entry(unrelated).Property(parent => parent.Name).IsModified.Should().BeTrue();
        unrelated.Name.Should().Be("Pending unrelated edit");
        fixture.Context.ChangeTracker.Entries<CollectionChild>().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
        fixture.Context.ChangeTracker.Entries<CollectionChild>().Select(entry => entry.Entity.Key).Should().BeEquivalentTo("A", "B");
        (await fixture.Context.Parents.SingleAsync(parent => parent.Id == 1)).Version.Should().Be(1);
        (await fixture.Context.Children.AsNoTracking().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task StageSupportsOneLaterSharedSaveAndOwnedLeasesRejectIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var model = Model(fixture.Context, leases: true);
        var provider = new ScopedDbContextLeaseProvider<CollectionContext>(fixture.Context);
        var operations = fixture.Operations(model, provider);
        (await Invoke<CommandResult>(operations, "StageAsync", Request(new("A", "Staged"), new("C", "New")))).Status.Should().Be(CommandStatus.Staged);
        (await fixture.Context.Children.AsNoTracking().CountAsync()).Should().Be(3);
        await fixture.Context.SaveChangesAsync();
        (await fixture.Context.Children.AsNoTracking().SingleAsync(child => child.Id == 1)).Text.Should().Be("Staged");
        var owned = new OwnedProvider(fixture.Connection);
        var ownedOperations = fixture.Operations(model, owned);
        Func<Task> stage = async () => await Invoke<CommandResult>(ownedOperations, "StageAsync", Request());
        await stage.Should().ThrowAsync<InvalidOperationException>().WithMessage("*borrowed*");
        owned.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task ExplicitTransactionRollbackUndoesTheEntireSavedCollectionCommand()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var work = new RepositoryUnitOfWork<CollectionContext>(fixture.Context);
        var operations = fixture.Operations(Model(fixture.Context, leases: true), work);
        await using var transaction = await work.BeginTransactionAsync();
        (await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request(new CollectionItem("C", "Temporary")))).Succeeded.Should().BeTrue();
        await transaction.RollbackAsync();
        (await fixture.Context.Children.AsNoTracking().Select(child => child.Key).ToListAsync()).Should().BeEquivalentTo("A", "B", "A");
        (await fixture.Context.Parents.AsNoTracking().SingleAsync(parent => parent.Id == 1)).Version.Should().Be(1);
    }

    [Fact]
    public async Task VisibilityPoliciesConstrainChildMutationEvenWithHiddenChildrenAlreadyTracked()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Context.Children.Where(child => child.Id == 2).ExecuteUpdateAsync(setters => setters.SetProperty(child => child.Hidden, true));
        await fixture.Context.Children.LoadAsync();
        var operations = fixture.Operations(Model(fixture.Context, visibleOnly: true));
        (await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request())).Succeeded.Should().BeTrue();
        (await fixture.Context.Children.AsNoTracking().SingleAsync(child => child.Id == 2)).Text.Should().Be("Second");
        (await fixture.Context.Children.CountAsync(child => child.ParentId == 1)).Should().Be(1);
    }

    [Fact]
    public async Task GlobalFiltersAlsoKeepHiddenTrackedChildrenOutsideTheMutationScope()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Context.Children.Where(child => child.Id == 2).ExecuteUpdateAsync(setters => setters.SetProperty(child => child.Hidden, true));
        await fixture.Context.Children.LoadAsync();
        fixture.Context.HideHiddenChildren = true;
        var operations = fixture.Operations(Model(fixture.Context));
        (await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request())).Succeeded.Should().BeTrue();
        (await fixture.Context.Children.IgnoreQueryFilters().AsNoTracking().SingleAsync(child => child.Id == 2)).Text.Should().Be("Second");
        (await fixture.Context.Children.IgnoreQueryFilters().CountAsync(child => child.ParentId == 1)).Should().Be(1);
    }

    [Fact]
    public async Task PendingChildChangesAreRejectedAndPreservedRatherThanSilentlyMerged()
    {
        await using var fixture = await Fixture.CreateAsync();
        var child = await fixture.Context.Children.SingleAsync(child => child.Id == 1);
        child.Text = "Pending existing edit";
        var operations = fixture.Operations(Model(fixture.Context));
        Func<Task> reconcile = async () => await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request());
        await reconcile.Should().ThrowAsync<InvalidOperationException>().WithMessage("*pending changes*");
        child.Text.Should().Be("Pending existing edit");
        fixture.Context.Entry(child).Property(item => item.Text).IsModified.Should().BeTrue();
    }

    [Fact]
    public async Task ReadOnlyNavigationCollectionsAreRejectedBeforeAnySettersOrMembershipChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var parent = await fixture.Context.Parents.Include(parent => parent.Children).SingleAsync(parent => parent.Id == 1);
        parent.Children = new System.Collections.ObjectModel.ReadOnlyCollection<CollectionChild>(parent.Children.ToList());
        var operations = fixture.Operations(Model(fixture.Context));
        Func<Task> reconcile = async () => await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request(new("A", "Forbidden"), new("C", "New")));
        await reconcile.Should().ThrowAsync<InvalidOperationException>().WithMessage("*writable*");
        parent.Version.Should().Be(1);
        parent.Children.Single(child => child.Key == "A").Text.Should().Be("Initial");
        fixture.Context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
    }

    [Fact]
    public async Task AFailedSecondCollectionRestoresTheFirstCollectionsChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder();
        builder.Entity<CollectionParent>().HasCommand("Both").Where<CollectionRequest>((parent, request) => parent.Id == request.ParentId)
            .ReconcileCollection<CollectionChild, CollectionItem, string, CollectionRequest>(parent => parent.Children, request => request.Items, child => child.Key, item => item.Key,
                children => children.Update(setters => setters.SetProperty(child => child.Text, (child, item) => item.Text)).MissingItems(RepositoryCollectionRemovalBehavior.Delete))
            .ReconcileCollection<CollectionAttachment, CollectionItem, string, CollectionRequest>(parent => parent.Attachments, request => request.Items, child => child.Key, item => item.Key,
                children => children.MissingItems(RepositoryCollectionRemovalBehavior.Delete)).Save();
        var operations = fixture.Operations(RepositoryModelFactory.Create(fixture.Context, typeof(CollectionContext).Assembly, design: builder.Build()));
        Func<Task> reconcile = async () => await Invoke<CommandResult>(operations, "BothAsync", Request(new CollectionItem("A", "Temporary")));
        await reconcile.Should().ThrowAsync<ArgumentException>().WithMessage("*unknown keys*");
        var parent = await fixture.Context.Parents.SingleAsync(parent => parent.Id == 1);
        parent.Children.Select(child => child.Key).Should().BeEquivalentTo("A", "B");
        parent.Children.Single(child => child.Key == "A").Text.Should().Be("Initial");
        fixture.Context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
    }

    [Fact]
    public async Task ParentOwnershipMismatchDoesNotLoadOrMutateChildren()
    {
        await using var fixture = await Fixture.CreateAsync();
        var operations = fixture.Operations(Model(fixture.Context));
        (await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request() with { OwnerId = 8 })).Status.Should().Be(CommandStatus.NotFound);
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
        (await fixture.Context.Children.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task PreCancelledCollectionCommandsNeverAcquireAnOwnedContext()
    {
        await using var fixture = await Fixture.CreateAsync();
        var provider = new OwnedProvider(fixture.Connection);
        var operations = fixture.Operations(Model(fixture.Context, leases: true), provider);
        Func<Task> cancelled = async () => await (Task<CommandResult<CollectionSummary>>)operations.GetType().GetMethod("ReplaceAsync")!
            .Invoke(operations, [Request(), new CancellationToken(canceled: true)])!;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        provider.Acquisitions.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImplicitLazyLoadingIsDisabledForCollectionCommandsAndRestoredOnSuccessAndFailure(bool previous)
    {
        await using var fixture = await Fixture.CreateAsync();
        var probe = new LazyLoadingProbe();
        await using var context = new CollectionContext(new DbContextOptionsBuilder<CollectionContext>().UseSqlite(fixture.Connection).AddInterceptors(probe).Options);
        context.ChangeTracker.LazyLoadingEnabled = previous;
        var operations = fixture.Operations(Model(context), context: context);
        (await Invoke<CommandResult<CollectionSummary>>(operations, "ReplaceAsync", Request(new CollectionItem("A", "Saved")))).Succeeded.Should().BeTrue();
        probe.EnabledDuringSave.Should().BeFalse();
        context.ChangeTracker.LazyLoadingEnabled.Should().Be(previous);
        Func<Task> failure = async () => await Invoke<CommandResult>(operations, "MergeAsync", Request(new CollectionItem("Unknown", "Forbidden")));
        await failure.Should().ThrowAsync<ArgumentException>();
        context.ChangeTracker.LazyLoadingEnabled.Should().Be(previous);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedChildReadsAreInvalidatedByAParentCollectionWrite(bool isolatedNamespace)
    {
        await using var fixture = await Fixture.CreateAsync();
        var builder = Builder().Configure(options => options.UseClosedCaching("collection-cache"));
        if (isolatedNamespace) builder.Configure(options => options.UseGeneratedNamespace("Collections.Persistence"));
        builder.Entity<CollectionParent>().HasCommand("Replace").Where<CollectionRequest>((parent, request) => parent.Id == request.ParentId)
            .ReconcileCollection<CollectionChild, CollectionItem, string, CollectionRequest>(parent => parent.Children, request => request.Items, child => child.Key, item => item.Key,
                children => children.Update(setters => setters.SetProperty(child => child.Text, (child, item) => item.Text)).MissingItems(RepositoryCollectionRemovalBehavior.Delete)).Save();
        builder.Entity<CollectionChild>().HasQuery("Texts").Cache().OrderBy(child => child.Id).Select(child => child.Text).List();
        var model = RepositoryModelFactory.Create(fixture.Context, typeof(CollectionContext).Assembly, design: builder.Build());
        var assembly = ClosedRepositoryTests.Compile(model);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new MemoryRepositoryResultCache(memory);
        var persistenceNamespace = model.Settings.GeneratedNamespace ?? typeof(CollectionParent).Namespace;
        var parent = Activator.CreateInstance(assembly.GetType(persistenceNamespace + ".CollectionParentRepositoryOperations")!, fixture.Context, cache)!;
        var child = Activator.CreateInstance(assembly.GetType(persistenceNamespace + ".CollectionChildRepositoryOperations")!, fixture.Context, cache)!;
        async Task<IReadOnlyList<string>> Read() => await (Task<IReadOnlyList<string>>)child.GetType().GetMethod("TextsAsync")!.Invoke(child, [CancellationToken.None])!;
        (await Read()).Should().Equal("Initial", "Second", "Other owner");
        (await Invoke<CommandResult>(parent, "ReplaceAsync", Request(new CollectionItem("A", "Invalidated")))).Succeeded.Should().BeTrue();
        (await Read()).Should().Equal("Invalidated", "Other owner");
    }

    [Fact]
    public void InventoryAndInvalidationIncludeChildrenEvenWithoutTheirOwnGeneratedRepository()
    {
        using var context = Context();
        var model = Model(context);
        var parent = model.Entities.Single();
        parent.CacheInvalidationTags.Should().Contain(typeof(CollectionChild).FullName!);
        var manifest = RepositoryManifestRenderer.Render(model);
        manifest.Should().Contain("\"collections\"").And.Contain("\"missingItems\": \"Delete\"").And.Contain("\"maximumItems\": 10");
        ClosedRepositorySourceRenderer.RenderContracts(model).Should().NotContain("DbContext").And.NotContain("CollectionChild");
    }

    [Theory]
    [InlineData("notClosed")]
    [InlineData("notPatch")]
    [InlineData("duplicateNavigation")]
    [InlineData("ownerWrite")]
    [InlineData("keyWrite")]
    [InlineData("graphCreate")]
    [InlineData("missingKey")]
    [InlineData("unconditionalParent")]
    [InlineData("unconditionalChild")]
    [InlineData("ignoredParentSetter")]
    [InlineData("ignoredChildSetter")]
    [InlineData("reload")]
    [InlineData("computedKey")]
    [InlineData("nonUniqueKey")]
    public void InvalidDesignerShapesFailDuringGeneration(string scenario)
    {
        using var context = Context();
        var builder = Builder(scenario != "notClosed");
        var command = builder.Entity<CollectionParent>().HasCommand("Invalid").Where<CollectionRequest>((parent, request) => parent.Id == request.ParentId);
        void Configure(RepositoryCollectionReconciliationBuilder<CollectionChild, CollectionItem> children)
        {
            children.Add(item => new CollectionChild { Key = item.Key, Text = item.Text });
            if (scenario == "ownerWrite") children.Update(setters => setters.SetProperty(child => child.ParentId, 2)).AllowWrite(child => child.ParentId);
            if (scenario == "keyWrite") children.Update(setters => setters.SetProperty(child => child.Key, "Changed"));
            if (scenario == "graphCreate") children.Add(item => new CollectionChild { Key = item.Key, Parent = new CollectionParent() });
            if (scenario == "missingKey") children.Add(item => new CollectionChild { Text = item.Text });
            if (scenario == "unconditionalChild") children.ExpectVersion(child => child.Version, (child, item) => item.ExpectedVersion);
            if (scenario == "ignoredChildSetter") children.Update(setters => setters.SetProperty(child => child.Locked, "Not persisted"))
                .ExpectVersion(child => child.Version, (child, item) => item.ExpectedVersion);
        }

        if (scenario == "computedKey")
        {
            command.ReconcileCollection<CollectionChild, CollectionItem, string, CollectionRequest>(parent => parent.Children, request => request.Items,
                child => child.Key.ToUpperInvariant(), item => item.Key, Configure);
        }
        else if (scenario == "nonUniqueKey")
        {
            command.ReconcileCollection<CollectionChild, CollectionItem, string, CollectionRequest>(parent => parent.Children, request => request.Items,
                child => child.ProtectedNote, item => item.Key, Configure);
        }
        else
        {
            command.ReconcileCollection<CollectionChild, CollectionItem, string, CollectionRequest>(parent => parent.Children, request => request.Items,
                child => child.Key, item => item.Key, Configure);
        }

        if (scenario == "duplicateNavigation") command.ReconcileCollection<CollectionChild, CollectionItem, string, CollectionRequest>(parent => parent.Children, request => request.Items, child => child.Key, item => item.Key, Configure);
        if (scenario == "notPatch") command.Delete();
        if (scenario == "unconditionalParent") command.ExpectVersion<int, CollectionRequest>(parent => parent.Version, (parent, request) => request.ExpectedVersion);
        if (scenario == "ignoredParentSetter") command.Patch(setters => setters.SetProperty(parent => parent.Locked, "Not persisted"))
            .ExpectVersion<int, CollectionRequest>(parent => parent.Version, (parent, request) => request.ExpectedVersion);
        if (scenario == "reload") command.ReloadAfterSave();
        command.Save();
        Action generate = () => RepositoryModelFactory.Create(context, typeof(CollectionContext).Assembly, design: builder.Build());
        var failure = generate.Should().Throw<InvalidOperationException>();
        if (scenario == "ignoredParentSetter") failure.WithMessage("RGD2801:*unconditional parent setter*");
        if (scenario == "ignoredChildSetter") failure.WithMessage("RGD2802:*unconditional child setter*");
    }

    private static CollectionRequest Request(params CollectionItem[] items) => new(1, 7, items, 1);
    private static CollectionContext Context() => new(new DbContextOptionsBuilder<CollectionContext>().UseSqlite("Data Source=:memory:").Options);
    private static RepositoryModelBuilder Builder(bool closed = true, bool leases = false)
    {
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (closed) builder.Configure(options => options.UseClosedRepositories("Generated.Collections"));
        if (leases) builder.Configure(options => options.UseAsyncContextLeases());
        return builder;
    }

    private static RepositoryGenerationModel Model(CollectionContext context, bool leases = false, int maximum = 10,
        RepositoryCollectionRemovalBehavior missing = RepositoryCollectionRemovalBehavior.Delete, bool visibleOnly = false)
    {
        var builder = Builder(leases: leases);
        var entity = builder.Entity<CollectionParent>();
        void Children(RepositoryCollectionReconciliationBuilder<CollectionChild, CollectionItem> children) => children
            .MaximumItems(maximum).MissingItems(missing)
            .Add(item => new CollectionChild { Key = item.Key, Text = item.Text })
            .Update(setters => setters.SetProperty(child => child.Text, (child, item) => item.Text)
                .SetProperty(child => child.Version, child => child.Version + 1))
            .AllowWrite(child => child.Version).ExpectVersion(child => child.Version, (child, item) => item.ExpectedVersion);
        foreach (var name in new[] { "Replace", "Stage" })
        {
            var command = entity.HasCommand(name).Where<CollectionRequest>((parent, request) => parent.Id == request.ParentId && parent.OwnerId == request.OwnerId)
                .Patch(setters => setters.SetProperty(parent => parent.Version, parent => parent.Version + 1)).AllowWrite(parent => parent.Version)
                .ExpectVersion<int, CollectionRequest>(parent => parent.Version, (parent, request) => request.ExpectedVersion)
                .ReconcileCollection<CollectionChild, CollectionItem, string, CollectionRequest>(parent => parent.Children, request => request.Items, child => child.Key, item => item.Key, Children);
            if (name == "Stage") command.Stage();
            else command.Save().Returns(parent => new CollectionSummary(parent.Version, parent.Children.Select(child => child.Key).ToArray()));
        }

        entity.HasCommand("Merge").Where<CollectionRequest>((parent, request) => parent.Id == request.ParentId && parent.OwnerId == request.OwnerId)
            .ReconcileCollection<CollectionChild, CollectionItem, string, CollectionRequest>(parent => parent.Children, request => request.Items, child => child.Key, item => item.Key,
                children => children.Update(setters => setters.SetProperty(child => child.Text, (child, item) => item.Text)))
            .Save();
        entity.HasCommand("Optional").Where<OptionalCollectionRequest>((parent, request) => parent.Id == request.ParentId)
            .ReconcileCollection<CollectionChild, CollectionItem, string, OptionalCollectionRequest>(parent => parent.Children, request => request.Items.Value!, child => child.Key, item => item.Key,
                children => children.When<OptionalCollectionRequest>(request => request.Items.IsSpecified).MissingItems(RepositoryCollectionRemovalBehavior.Delete))
            .Save();
        if (visibleOnly)
        {
            var children = builder.Entity<CollectionChild>();
            children.RepositoryEnabled = false;
            children.HasVisibilityPolicy("visible", child => !child.Hidden);
        }
        return RepositoryModelFactory.Create(context, typeof(CollectionContext).Assembly, design: builder.Build());
    }

    private static Task<T> Invoke<T>(object operations, string method, object request) => (Task<T>)operations.GetType().GetMethod(method)!.Invoke(operations, [request, CancellationToken.None])!;

    private sealed class Fixture(CollectionContext context, SqliteConnection connection) : IAsyncDisposable
    {
        public CollectionContext Context { get; } = context;
        public SqliteConnection Connection { get; } = connection;
        public object Operations(RepositoryGenerationModel model, IDbContextLeaseProvider<CollectionContext>? provider = null, CollectionContext? context = null)
        {
            var assembly = ClosedRepositoryTests.Compile(model);
            return Activator.CreateInstance(assembly.GetType(typeof(CollectionParent).Namespace + ".CollectionParentRepositoryOperations")!, provider is null ? context ?? Context : provider)!;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new CollectionContext(new DbContextOptionsBuilder<CollectionContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            context.Parents.AddRange(new CollectionParent { Id = 1, OwnerId = 7, Name = "First", Children = [new() { Id = 1, Key = "A", Text = "Initial", ProtectedNote = "Keep this" }, new() { Id = 2, Key = "B", Text = "Second" }] },
                new CollectionParent { Id = 2, OwnerId = 8, Name = "Other", Children = [new() { Id = 3, Key = "A", Text = "Other owner" }] });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            return new(context, connection);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class OwnedProvider(SqliteConnection connection) : IDbContextLeaseProvider<CollectionContext>
    {
        public int Acquisitions { get; private set; }
        private readonly List<CollectionContext> contexts = [];
        public int Disposals => contexts.Sum(context => context.DisposeCount);
        public ValueTask<DbContextLease<CollectionContext>> AcquireAsync(CancellationToken cancellationToken = default)
        {
            Acquisitions++;
            var context = new CollectionContext(new DbContextOptionsBuilder<CollectionContext>().UseSqlite(connection).Options);
            contexts.Add(context);
            return ValueTask.FromResult(DbContextLease.Owned(context));
        }
    }

    private sealed class ChildRace : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await eventData.Context!.Database.ExecuteSqlRawAsync("UPDATE Children SET Version = Version + 1 WHERE Id = 1", cancellationToken);
            return result;
        }
    }

    private sealed class LazyLoadingProbe : SaveChangesInterceptor
    {
        public bool? EnabledDuringSave { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            EnabledDuringSave = eventData.Context!.ChangeTracker.LazyLoadingEnabled;
            return ValueTask.FromResult(result);
        }
    }
}
