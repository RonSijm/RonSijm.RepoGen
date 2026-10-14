using System.Reflection;
using System.Runtime.Loader;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MultiContextGenerationTests
{
    [Theory]
    [InlineData("compatibility", RepositoryOutputLayout.SingleFile)]
    [InlineData("compatibility", RepositoryOutputLayout.PerEntity)]
    [InlineData("cqrs", RepositoryOutputLayout.SingleFile)]
    [InlineData("cqrs", RepositoryOutputLayout.PerEntity)]
    [InlineData("closed", RepositoryOutputLayout.SingleFile)]
    [InlineData("closed", RepositoryOutputLayout.PerEntity)]
    [InlineData("closed", RepositoryOutputLayout.PerContract)]
    public void EverySurfaceAndLayoutCompilesWithExplicitPlacementAndCompleteTypeInventory(string mode, RepositoryOutputLayout layout)
    {
        using var context = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        var bundle = RepositoryGenerator.Generate(context, Design("Multi.Primary", mode), layout);
        var assembly = Compile(bundle);
        RepositoryGeneratedTypes.Names(bundle.Model).Should().BeEquivalentTo(assembly.GetTypes()
            .Where(static type => !type.IsNested && !type.Name.StartsWith('<')).Select(static type => type.FullName!));
        var sources = string.Join("\n", Sources(bundle));
        sources.Should().Contain("namespace Multi.Primary").And.NotContain("global::RonSijm.RepoGen.Base");
        sources.Should().Contain("global::" + typeof(MultiRow).FullName); // Models/DTOs keep their real CLR identities.
        bundle.Files.Single(static file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("\"generatedNamespace\": \"Multi.Primary\"");
        if (mode == "closed")
        {
            var contracts = string.Join("\n", bundle.Files.Where(static file => file.Kind == RepositoryOutputKind.Contracts).Select(static file => file.Contents));
            contracts.Should().Contain("RonSijm.RepoGen.Abstractions.PagedResult").And.NotContain("Multi.Primary.PagedResult").And.NotContain("EntityFrameworkCore");
            bundle.CreateApiSnapshot().ToJson().Should().Contain("Abstractions.CursorPageRequest").And.NotContain("Multi.Primary.PagedResult");
        }
    }

    [Fact]
    public async Task SameEntityInTwoContextsUsesIndependentDiPortsContextsTrackingAndSaves()
    {
        await using var primaryConnection = new SqliteConnection("Data Source=:memory:");
        await primaryConnection.OpenAsync();
        await using var replicaConnection = new SqliteConnection("Data Source=:memory:");
        await replicaConnection.OpenAsync();
        await using var primary = new MultiPrimaryContext(Options<MultiPrimaryContext>(primaryConnection));
        await using var replica = new MultiReplicaContext(Options<MultiReplicaContext>(replicaConnection));
        await Seed(primary, "primary");
        await Seed(replica, "replica");
        var batch = RepositoryGenerator.GenerateMany(
            new(primary, Locations("primary"), Design("Multi.Primary", "cqrs")),
            new(replica, Locations("replica"), Design("Multi.Replica", "closed")));
        var assembly = Compile(batch.Bundles.ToArray());
        var services = new ServiceCollection();
        services.AddSingleton(primary);
        services.AddSingleton(replica);
        assembly.GetType("Multi.Primary.MultiPrimaryContextSelectorRepositoryRegistration")!.GetMethod("AddMultiPrimaryContextSelectorRepositories")!
            .Invoke(null, [services, false]);
        assembly.GetType("Multi.Replica.Contracts.MultiReplicaContextRepositoryRegistration")!.GetMethod("AddMultiReplicaContextRepositories")!.Invoke(null, [services]);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var reads = scope.ServiceProvider.GetRequiredService(assembly.GetType("Multi.Primary.IReadMultiRowRepository")!);
        var writes = scope.ServiceProvider.GetRequiredService(assembly.GetType("Multi.Primary.IWriteMultiRowRepository")!);
        var replicaReads = scope.ServiceProvider.GetRequiredService<IMultiReplicaRead>();
        (await Call<Task<IReadOnlyList<MultiRowSummary>>>(reads, "ListAsync", CancellationToken.None)).Single().Name.Should().Be("primary");
        (await replicaReads.ListAsync()).Single().Name.Should().Be("replica");
        var page = await replicaReads.PageAsync(1, 10);
        page.Items.Single().Name.Should().Be("replica");
        (await replicaReads.CursorAsync(new CursorPageRequest<int>(10))).Items.Should().HaveCount(1);
        primary.ChangeTracker.Entries().Should().BeEmpty();
        replica.ChangeTracker.Entries().Should().BeEmpty();
        var selected = Call<object>(reads, "ById", 1);
        (await Call<Task<MultiRow?>>(selected, "AsEntityAsync", CancellationToken.None))!.Name.Should().Be("primary");
        selected.GetType().GetMethod("Delete").Should().BeNull();
        Call<object?>(writes, "Add", new MultiRow { Id = 2, Name = "new primary" });
        (await Call<Task<int>>(writes, "SaveChangesAsync", CancellationToken.None)).Should().Be(1);
        (await replicaReads.ListAsync()).Should().HaveCount(1);
        (await primary.Set<MultiRow>().CountAsync()).Should().Be(2);
        batch.Check().Should().OnlyContain(static difference => difference.DiagnosticId == "RGG0001");
    }

    [Fact]
    public async Task RelocatedPagedAndCursorPipelinesUseTheCorrectResultTypes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MultiPrimaryContext(Options<MultiPrimaryContext>(connection));
        await Seed(context, "one");
        var bundle = RepositoryGenerator.Generate(context, Design("Multi.Pipelines", "cqrs"));
        var assembly = Compile(bundle);
        var source = Activator.CreateInstance(assembly.GetType("Multi.Pipelines.MultiRowRepository")!, context)!;
        var read = Activator.CreateInstance(assembly.GetType("Multi.Pipelines.MultiRowReadRepository")!, source)!;
        foreach (var name in new[] { "PageAsync", "PipelinePageAsync" })
        {
            var task = Call<Task>(read, name, 1, 10, CancellationToken.None);
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            result.GetType().Namespace.Should().Be("Multi.Pipelines");
            ((IReadOnlyList<MultiRowSummary>)result.GetType().GetProperty("Items")!.GetValue(result)!).Should().HaveCount(1);
        }

        var cursorType = assembly.GetType("Multi.Pipelines.CursorPageRequest`1")!.MakeGenericType(typeof(int));
        var cursor = cursorType.GetMethod("First")!.Invoke(null, [10])!;
        foreach (var name in new[] { "CursorAsync", "PipelineCursorAsync" })
        {
            var task = Call<Task>(read, name, cursor, CancellationToken.None);
            await task;
            task.GetType().GetProperty("Result")!.GetValue(task)!.GetType().Namespace.Should().Be("Multi.Pipelines");
        }
    }

    [Fact]
    public void RelocationDoesNotChangeApplicationContractsOrRewriteUserExpressionsOrLiterals()
    {
        using var context = new MultiReplicaContext(Options<MultiReplicaContext>());
        var before = RepositoryGenerator.Generate(context, Design(null, "closed", contracts: "Stable.Contracts"));
        var after = RepositoryGenerator.Generate(context, Design("Multi.Moved", "closed", contracts: "Stable.Contracts"));
        before.CreateApiSnapshot().ToJson().Should().Be(after.CreateApiSnapshot().ToJson());
        var source = string.Join("\n", Sources(after));
        source.Should().Contain("global::RonSijm.RepoGen.PagedResult<literal>");
        source.Should().Contain("global::" + typeof(MultiRowSummary).FullName);
        after.Model.Entities.Single().Namespace.Should().Be("Multi.Moved");
    }

    [Fact]
    public void PresetsKeepExplicitNamespaceAndNullRestoresTheByteIdenticalDefaults()
    {
        using var context = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseGeneratedNamespace("Multi.Keep").UseSimpleDefaults());
        builder.Build().Options.GeneratedNamespace.Should().Be("Multi.Keep");
        builder.Configure(options => options.UseCompatibilityDefaults());
        builder.Build().Options.GeneratedNamespace.Should().Be("Multi.Keep");
        builder.Configure(options => options.UseGeneratedNamespace(null));
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().Equal(RepositoryGenerator.Generate(context).Files);
    }

    [Theory]
    [InlineData("Bad..Namespace")]
    [InlineData("Bad.class")]
    [InlineData("Bad.@class")]
    [InlineData("Bad;Injected")]
    [InlineData("2Bad.Namespace")]
    [InlineData(" Bad.Namespace")]
    [InlineData("Bad.__arglist")]
    [InlineData("Bad.__makeref")]
    [InlineData("Bad.__refvalue")]
    [InlineData("Bad.__reftype")]
    [InlineData("RonSijm.RepoGen.Abstractions")]
    [InlineData("RonSijm.RepoGen.EntityFrameworkCore.Nested")]
    public void InvalidAndReservedNamespacesAreRejected(string value)
    {
        using var context = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        Action generate = () => RepositoryGenerator.Generate(context, Design(value, "cqrs"));
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1801:*");
    }

    [Fact]
    public void IsolatedPlacementCannotReuseUnrelatedExistingSelectors()
    {
        using var context = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        // Follow the fixture's namespace so source organization cannot silently bypass the reuse guard.
        Action generate = () => RepositoryGenerator.Generate(context, Design(typeof(MultiRowSelector).Namespace, "compatibility"), generateSelectors: false);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1802:*");
    }

    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    public void GenericCompatibilityContextsHaveValidNamesAndCompilableIsolatedOutput(RepositoryOutputLayout layout)
    {
        using var context = new GenericMultiContext<MultiRow>(Options<GenericMultiContext<MultiRow>>());
        var bundle = RepositoryGenerator.Generate(context, Design("Multi.Generic", "compatibility"), layout);
        Compile(bundle);
        bundle.Files.Should().OnlyContain(file => file.Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        if (layout == RepositoryOutputLayout.PerEntity)
        {
            bundle.Files.Should().Contain(file => file.Name == "Multi.Generic.GenericMultiContext.RepositoryInfrastructure.g.cs");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateInfrastructureOrContractTypesAreRejectedBeforeWriting(bool contracts)
    {
        using var first = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        using var second = new MultiReplicaContext(Options<MultiReplicaContext>());
        var root = Temporary();
        Action generate = () => RepositoryGenerator.GenerateMany(
            new(first, Locations(root, "first"), Design(contracts ? "Multi.First" : null, contracts ? "closed" : "compatibility", "Shared.Contracts")),
            new(second, Locations(root, "second"), Design(contracts ? "Multi.Second" : null, contracts ? "closed" : "compatibility", "Shared.Contracts")));
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGG0007:*");
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void SharedBoundPortIsDiagnosedEvenWhenGeneratedTypesAreDistinct()
    {
        using var first = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        using var second = new MultiReplicaContext(Options<MultiReplicaContext>());
        Action generate = () => RepositoryGenerator.GenerateMany(
            new(first, Locations("first"), Design("Multi.First", "closed")), new(second, Locations("second"), Design("Multi.Second", "closed")));
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGG0009:*");
    }

    [Theory]
    [InlineData("same")]
    [InlineData("case")]
    [InlineData("parent")]
    public void CrossContextPathAndParentCollisionsAreDiagnosedWithoutDirectories(string scenario)
    {
        using var first = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        using var second = new MultiReplicaContext(Options<MultiReplicaContext>());
        var root = Temporary();
        var path = Path.Combine(root, "repo.g.cs");
        var other = scenario == "case" ? Path.Combine(root, "REPO.g.cs") : scenario == "parent" ? Path.Combine(path, "child.g.cs") : path;
        Action generate = () => RepositoryGenerator.GenerateMany(
            new(first, new(path, Projections: Path.Combine(root, "first.queries.g.cs")), Design("Multi.First", "cqrs")),
            new(second, new(other, Projections: Path.Combine(root, "second.queries.g.cs")), Design("Multi.Second", "cqrs")));
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGG0002:*");
        Directory.Exists(root).Should().BeFalse();
    }

    [Fact]
    public void WholeBatchPreflightPreservesEarlierOutputsWhenALaterContextHasHandwrittenCode()
    {
        using var first = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        using var second = new MultiReplicaContext(Options<MultiReplicaContext>());
        var root = Temporary();
        var output = Locations(root, "second");
        var batch = RepositoryGenerator.GenerateMany(new(first, Locations(root, "first"), Design("Multi.First", "cqrs")), new(second, output, Design("Multi.Second", "cqrs")));
        batch.Check().Should().HaveCount(6);
        Directory.Exists(root).Should().BeFalse();
        Directory.CreateDirectory(Path.GetDirectoryName(output.Repositories)!);
        try
        {
            File.WriteAllText(output.Repositories, "// handwritten");
            Action write = () => batch.Write();
            write.Should().Throw<InvalidOperationException>().WithMessage("RGG0003:*");
            File.Exists(Locations(root, "first").Repositories).Should().BeFalse();
            File.ReadAllText(output.Repositories).Should().Be("// handwritten");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(RepositoryOutputKind.Repositories)]
    [InlineData(RepositoryOutputKind.Manifest)]
    [InlineData(RepositoryOutputKind.ApiSnapshot)]
    public void AnotherContextsOwnedOutputCannotBeOverwritten(RepositoryOutputKind kind)
    {
        using var first = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        using var second = new MultiReplicaContext(Options<MultiReplicaContext>());
        var original = RepositoryGenerator.Generate(first, Design("Multi.First", "closed")).WithApiSnapshot();
        var next = RepositoryGenerator.Generate(second, Design("Multi.Second", "closed")).WithApiSnapshot();
        var root = Temporary();
        var output = Locations(root, "test");
        Directory.CreateDirectory(Path.GetDirectoryName(output.Repositories)!);
        var path = kind == RepositoryOutputKind.Repositories ? output.Repositories : kind == RepositoryOutputKind.Manifest ? output.Manifest! : output.ApiSnapshot!;
        var contents = original.Files.Single(file => file.Kind == kind).Contents;
        try
        {
            File.WriteAllText(path, contents);
            Action write = () => next.Write(output);
            write.Should().Throw<InvalidOperationException>().WithMessage("RGG0008:*");
            File.ReadAllText(path).Should().Be(contents);
            Directory.GetFiles(root, "*", SearchOption.AllDirectories).Should().ContainSingle();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SharedSplitDirectoriesAreDeterministicFreshAndReportObsoleteFilesPerContext()
    {
        using var first = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        using var second = new MultiReplicaContext(Options<MultiReplicaContext>());
        var root = Temporary();
        var firstOutput = new RepositoryOutputLocations(Path.Combine(root, "same"), Projections: Path.Combine(root, "same"), Manifest: Path.Combine(root, "first.json"));
        var secondOutput = firstOutput with { Manifest = Path.Combine(root, "second.json") };
        // Split infrastructure filenames are context-qualified when placement is explicit.
        var requests = new[]
        {
            new RepositoryGenerationRequest(first, firstOutput, Design("Multi.First", "cqrs"), RepositoryOutputLayout.PerEntity),
            new RepositoryGenerationRequest(second, secondOutput, Design("Multi.Second", "cqrs"), RepositoryOutputLayout.PerEntity)
        };
        var batch = RepositoryGenerator.GenerateMany(requests);
        batch.Bundles.SelectMany(static bundle => bundle.Files).Should().Equal(RepositoryGenerator.GenerateMany(requests.Reverse()).Bundles.SelectMany(static bundle => bundle.Files));
        try
        {
            batch.Write().Should().HaveCount(8);
            batch.Check().Should().BeEmpty();
            batch.Write().Should().BeEmpty();
            var old = Path.Combine(root, "same", "Old.g.cs");
            File.WriteAllText(old, batch.Bundles[0].Files.First(static file => file.Kind == RepositoryOutputKind.Repositories).Contents);
            batch.Check().Should().ContainSingle().Which.DiagnosticId.Should().Be("RGG0004");
            batch.Write().Should().BeEmpty();
            File.Exists(old).Should().BeTrue();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CacheTagsAreIsolatedForSharedEntitiesAndResolverCacheTypesMoveTogether()
    {
        using var first = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        using var second = new MultiReplicaContext(Options<MultiReplicaContext>());
        var design = Design("Multi.First", "cqrs");
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseCqrsSelectors().UseGeneratedNamespace("Multi.Resolver").UseDbContextResolver());
        builder.Entity<MultiRow>(entity => entity.HasQuery("Cached").ProjectTo(row => row.Name).Cache().List());
        var bundle = RepositoryGenerator.Generate(first, builder.Build());
        Compile(bundle);
        var source = string.Join("\n", Sources(bundle));
        source.Should().Contain("Multi.Resolver.IDbContextResolver<").And.Contain("Multi.Resolver.IRepositoryCacheProvider").And.Contain("Multi.Resolver.RepositoryCacheEntryOptions");
        var firstTag = RepositoryGenerator.Generate(first, design).Model.Entities.Single().CacheTag;
        var secondTag = RepositoryGenerator.Generate(second, design).Model.Entities.Single().CacheTag;
        firstTag.Should().NotBe(secondTag);
    }

    [Fact]
    public async Task DecoratedRepositoriesSharingABackendDoNotShareRowsOrInvalidateTheOtherDatabase()
    {
        await using var a = new SqliteConnection("Data Source=:memory:");
        await a.OpenAsync();
        await using var b = new SqliteConnection("Data Source=:memory:");
        await b.OpenAsync();
        await using var primary = new MultiPrimaryContext(Options<MultiPrimaryContext>(a));
        await using var replica = new MultiReplicaContext(Options<MultiReplicaContext>(b));
        await Seed(primary, "primary");
        await Seed(replica, "replica");

        static RepositoryDesign Cached(string ns)
        {
            var builder = new RepositoryModelBuilder();
            builder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors().UseGeneratedNamespace(ns).GenerateCacheDecorator());
            builder.Entity<MultiRow>(entity => entity.HasQuery("Names").ProjectTo(row => row.Name).Cache().List());
            return builder.Build();
        }

        var first = RepositoryGenerator.Generate(primary, Cached("Multi.CachedPrimary"));
        var second = RepositoryGenerator.Generate(replica, Cached("Multi.CachedReplica"));
        var assembly = Compile(first, second);
        var entries = new Dictionary<string, (string Tag, object Task)>();
        var services = new ServiceCollection();
        services.AddSingleton(primary);
        services.AddSingleton(replica);
        var caches = new List<MultiContextCacheProxy>();
        foreach (var (ns, context) in new[] { ("Multi.CachedPrimary", "MultiPrimaryContext"), ("Multi.CachedReplica", "MultiReplicaContext") })
        {
            var type = assembly.GetType(ns + ".IRepositoryCacheProvider")!;
            var proxy = DispatchProxy.Create(type, typeof(MultiContextCacheProxy));
            ((MultiContextCacheProxy)proxy).Entries = entries;
            caches.Add((MultiContextCacheProxy)proxy);
            services.AddSingleton(type, proxy);
            assembly.GetType(ns + "." + context + "SelectorRepositoryRegistration")!.GetMethod("Add" + context + "SelectorRepositories")!.Invoke(null, [services, true]);
        }

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var primaryRead = scope.ServiceProvider.GetRequiredService(assembly.GetType("Multi.CachedPrimary.IReadMultiRowRepository")!);
        var replicaRead = scope.ServiceProvider.GetRequiredService(assembly.GetType("Multi.CachedReplica.IReadMultiRowRepository")!);
        for (var index = 0; index < 2; index++)
        {
            (await Call<Task<IReadOnlyList<string>>>(primaryRead, "NamesAsync", CancellationToken.None)).Should().Equal("primary");
            (await Call<Task<IReadOnlyList<string>>>(replicaRead, "NamesAsync", CancellationToken.None)).Should().Equal("replica");
        }

        caches.Select(static cache => cache.FactoryCalls).Should().Equal(1, 1);
        var write = scope.ServiceProvider.GetRequiredService(assembly.GetType("Multi.CachedPrimary.IWriteMultiRowRepository")!);
        Call<object?>(write, "Add", new MultiRow { Id = 2, Name = "new primary" });
        await Call<Task<int>>(write, "SaveChangesAsync", CancellationToken.None);
        (await Call<Task<IReadOnlyList<string>>>(primaryRead, "NamesAsync", CancellationToken.None)).Should().BeEquivalentTo("primary", "new primary");
        (await Call<Task<IReadOnlyList<string>>>(replicaRead, "NamesAsync", CancellationToken.None)).Should().Equal("replica");
        caches.Select(static cache => cache.FactoryCalls).Should().Equal(2, 1);
        caches.Select(static cache => cache.Invalidations).Should().Equal(1, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlannedOrExistingParentFilesArePreflightedBeforeAnyOutput(bool existing)
    {
        using var context = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        var bundle = RepositoryGenerator.Generate(context, Design("Multi.Parent", "cqrs"));
        var root = Temporary();
        var parent = Path.Combine(root, "parent");
        var output = new RepositoryOutputLocations(existing ? Path.Combine(root, "first.g.cs") : parent,
            Projections: Path.Combine(parent, "child.g.cs"));
        if (existing)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(parent, "keep");
        }

        try
        {
            Action write = () => bundle.Write(output);
            write.Should().Throw<InvalidOperationException>().WithMessage("RGG0002:*");
            if (existing)
            {
                File.ReadAllText(parent).Should().Be("keep");
                File.Exists(output.Repositories).Should().BeFalse();
            }
            else
            {
                Directory.Exists(root).Should().BeFalse();
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReplacementAndGroupedRootsRemainServerComposedAfterNamespaceRelocation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new MultiPrimaryContext(Options<MultiPrimaryContext>(connection));
        await Seed(context, "one");
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors().UseGeneratedNamespace("Multi.Roots"));
        builder.Entity<MultiRow>(entity =>
        {
            entity.HasQuery("FromPage").From(sources => sources.Set<MultiRow>().OrderBy(row => row.Id).Select(row => row.Name)).Paged();
            entity.HasQuery("Groups").GroupBy(row => row.Name, group => new MultiRowSummary { Id = group.Count(), Name = group.Key }).OrderBy(row => row.Name).Paged();
        });
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var assembly = Compile(bundle);
        var source = Activator.CreateInstance(assembly.GetType("Multi.Roots.MultiRowRepository")!, context)!;
        var read = Activator.CreateInstance(assembly.GetType("Multi.Roots.MultiRowReadRepository")!, source)!;
        foreach (var name in new[] { "FromPageAsync", "GroupsAsync" })
        {
            var task = Call<Task>(read, name, 1, 10, CancellationToken.None);
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            result.GetType().Namespace.Should().Be("Multi.Roots");
            result.GetType().GetProperty("TotalCount")!.GetValue(result).Should().Be(1);
        }

        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void RepeatedContextsEmptyAndNullRequestsAreRejected()
    {
        using var context = new MultiPrimaryContext(Options<MultiPrimaryContext>());
        Action duplicate = () => RepositoryGenerator.GenerateMany(new(context, Locations("first"), Design("Multi.First", "cqrs")), new(context, Locations("second"), Design("Multi.Second", "cqrs")));
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("RGG0010:*");
        Action empty = () => RepositoryGenerator.GenerateMany(Array.Empty<RepositoryGenerationRequest>());
        empty.Should().Throw<ArgumentException>();
        Action missing = () => RepositoryGenerator.GenerateMany(new RepositoryGenerationRequest[] { null! });
        missing.Should().Throw<ArgumentNullException>();
    }

    private static RepositoryDesign Design(string? ns, string mode, string? contracts = null)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options =>
        {
            options.UseSimpleDefaults().GenerateOnlyConfiguredEntities().UseGeneratedNamespace(ns).GenerateCacheDecorator(mode != "closed");
            if (mode == "closed")
            {
                options.UseClosedRepositories(contracts ?? ns + ".Contracts");
            }

            if (mode == "cqrs")
            {
                options.UseCqrsSelectors();
            }
        });
        builder.Entity<MultiRow>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasProjection<MultiRowSummary>(row => new MultiRowSummary(row.Id, row.Name));
            entity.HasQuery("List").OrderBy(row => row.Id).ProjectTo<MultiRowSummary>().List();
            entity.HasQuery("Page").OrderBy(row => row.Id).ProjectTo<MultiRowSummary>().Paged();
            entity.HasQuery("Cursor").OrderBy(row => row.Id).ProjectTo<MultiRowSummary>().CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
            entity.HasQuery("PipelinePage").Select(row => new MultiRowSummary { Id = row.Id, Name = row.Name }).OrderBy(row => row.Id).Paged();
            entity.HasQuery("PipelineCursor").Select(row => new MultiRowSummary { Id = row.Id, Name = row.Name }).OrderBy(row => row.Id).CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
            entity.HasQuery("Literal").Select(row => "global::RonSijm.RepoGen.PagedResult<literal>").List();
            if (mode == "closed")
            {
                entity.WriteEnabled = false;
                entity.BindReadPort<IMultiReplicaRead>();
            }
        });
        return builder.Build();
    }

    private static IEnumerable<string> Sources(RepositoryGenerationBundle bundle) => bundle.Files.Where(static file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts or RepositoryOutputKind.Projections).Select(static file => file.Contents);

    private static Assembly Compile(params RepositoryGenerationBundle[] bundles)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(MultiContextGenerationTests).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("Multi_" + Guid.NewGuid().ToString("N"), bundles.SelectMany(Sources).Select(static source => CSharpSyntaxTree.ParseText(source)),
            paths.Select(static path => MetadataReference.CreateFromFile(path)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        result.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error || diagnostic.Severity == DiagnosticSeverity.Warning && diagnostic.Id != "CS1701").Should().BeEmpty();
        output.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(output);
    }

    private static string Temporary() => Path.Combine(Path.GetTempPath(), "repogen-multi-" + Guid.NewGuid().ToString("N"));
    private static RepositoryOutputLocations Locations(string name) => Locations(Temporary(), name);

    private static RepositoryOutputLocations Locations(string root, string name) => new(Path.Combine(root, name, "Repositories.g.cs"),
        Path.Combine(root, name, "Contracts.g.cs"), Path.Combine(root, name, "Queries.g.cs"), Path.Combine(root, name, "manifest.json"), Path.Combine(root, name, "api.json"));

    private static DbContextOptions<T> Options<T>(SqliteConnection? connection = null) where T : DbContext => connection is null
        ? new DbContextOptionsBuilder<T>().UseSqlite("Data Source=:memory:").Options
        : new DbContextOptionsBuilder<T>().UseSqlite(connection).Options;

    private static T Call<T>(object value, string method, params object[] arguments) => (T)value.GetType().GetMethods().Single(info => info.Name == method && !info.IsGenericMethod && info.GetParameters().Length == arguments.Length).Invoke(value, arguments)!;

    private static async Task Seed(DbContext context, string name)
    {
        await context.Database.EnsureCreatedAsync();
        context.Add(new MultiRow { Id = 1, Name = name });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }
}

public sealed class MultiRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed record MultiRowSummary(int Id, string Name)
{
    public MultiRowSummary() : this(0, "") { }
}

public sealed class MultiPrimaryContext(DbContextOptions<MultiPrimaryContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<MultiRow>();
}

public sealed class MultiReplicaContext(DbContextOptions<MultiReplicaContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<MultiRow>();
}

public sealed class GenericMultiContext<TEntity>(DbContextOptions<GenericMultiContext<TEntity>> options) : DbContext(options) where TEntity : class
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<TEntity>();
}

public interface IMultiReplicaRead
{
    Task<IReadOnlyList<MultiRowSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<MultiRowSummary>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<MultiRowSummary, int>> CursorAsync(CursorPageRequest<int> cursorPage, CancellationToken cancellationToken = default);
}

public class MultiContextCacheProxy : DispatchProxy
{
    public Dictionary<string, (string Tag, object Task)> Entries { get; set; } = [];
    public int FactoryCalls { get; private set; }
    public int Invalidations { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == "InvalidateTagAsync")
        {
            Invalidations++;
            foreach (var key in Entries.Where(entry => entry.Value.Tag == (string)args![0]!).Select(static entry => entry.Key).ToArray())
            {
                Entries.Remove(key);
            }

            return Task.CompletedTask;
        }

        var cacheKey = (string)args![0]!;
        if (Entries.TryGetValue(cacheKey, out var cached))
        {
            return cached.Task;
        }

        FactoryCalls++;
        var task = args.OfType<Delegate>().Single().DynamicInvoke(args[^1])!;
        Entries.Add(cacheKey, ((string)args[1]!, task));
        return task;
    }
}
