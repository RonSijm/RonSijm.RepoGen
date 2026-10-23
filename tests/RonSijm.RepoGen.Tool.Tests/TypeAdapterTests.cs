using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class TypeAdapterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdaptedAccessorsCoverPrimaryAndCompositeIndexesWithoutChangingOtherProperties(bool cqrs)
    {
        await using var context = Context();
        await Seed(context);
        var builder = Builder(cqrs: cqrs);
        var id = builder.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        var entity = builder.Entity<AdapterRow>();
        entity.HasAccessor(row => row.Id).UseAdapter(id);
        entity.HasAccessor(row => row.Name).HasMethodName("ByTitle");
        var model = Model(context, builder);
        var row = model.Entities.Single();
        row.Lookups.Single(lookup => lookup.MethodName == "ById").IsUnique.Should().BeTrue();
        row.Lookups.Single(lookup => lookup.MethodName == "ByScopeAndId").Properties.Select(property => property.TypeName)
            .Should().Equal("global::System.Int32", RepositoryModelFactory.CSharpTypeName(typeof(ApplicationRowId)));
        row.Lookups.Single(lookup => lookup.MethodName == "ByTitle").Properties.Single().TypeName.Should().Be("global::System.String");
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var repository = Activator.CreateInstance(TypeOf(assembly, "AdapterRowRepository"), context)!;
        var selected = Invoke<object>(repository, "ById", new ApplicationRowId(1));
        (await Invoke<Task<AdapterRow?>>(selected, "AsEntityAsync", CancellationToken.None))!.Name.Should().Be("one");
        var composite = Invoke<object>(repository, "ByScopeAndId", 7, new ApplicationRowId(2));
        (await Invoke<Task<IReadOnlyList<AdapterRow>>>(composite, "AsEntitiesAsync", CancellationToken.None)).Should().ContainSingle().Which.Id.Should().Be(2);
        (await Invoke<Task<bool>>(Invoke<object>(repository, "ById", new ApplicationRowId(999)), "ExistsAsync", CancellationToken.None)).Should().BeFalse();
        var query = (IQueryable<AdapterRow>)selected.GetType().BaseType!.GetProperty("Query", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(selected)!;
        query.ToQueryString().Should().Contain("WHERE").And.Contain(".param set");
        if (cqrs)
        {
            var read = Activator.CreateInstance(TypeOf(assembly, "AdapterRowReadRepository"), repository)!;
            (await Invoke<Task<AdapterRow?>>(Invoke<object>(read, "ById", new ApplicationRowId(1)), "AsEntityAsync", CancellationToken.None))!.Id.Should().Be(1);
            Invoke<object>(read, "ById", new ApplicationRowId(1)).GetType().GetMethod("Delete").Should().BeNull();
        }
    }

    [Fact]
    public async Task CompositePrimaryKeysAndExplicitUniquenessOverridesUseAdaptedInputs()
    {
        await using var context = Context();
        await Seed(context);
        var builder = Builder();
        var id = builder.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        var entity = builder.Entity<AdapterLink>();
        entity.HasAccessor(link => link.Number).UseAdapter(id).HasMethodName("ByNumberAsId");
        var assembly = ClosedRepositoryTests.Compile(Model(context, builder), rejectWarnings: true);
        var repository = Activator.CreateInstance(TypeOf(assembly, "AdapterLinkRepository"), context)!;
        (await Invoke<Task<AdapterLink?>>(Invoke<object>(repository, "ByScopeAndNumber", 7, new ApplicationRowId(2)), "AsEntityAsync", CancellationToken.None))!.Label.Should().Be("link");
        (await Invoke<Task<IReadOnlyList<AdapterLink>>>(Invoke<object>(repository, "ByNumberAsId", new ApplicationRowId(2)), "AsEntitiesAsync", CancellationToken.None)).Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AdapterAccessorUniquenessCanBeExplicitlyOverridden(bool unique)
    {
        using var context = Context();
        var builder = Builder();
        var id = builder.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        builder.Entity<AdapterRow>().HasAccessor(row => row.Id).UseAdapter(id).IsUnique(unique);
        Model(context, builder).Entities.Single().Lookups.Single(lookup => lookup.MethodName == "ById").IsUnique.Should().Be(unique);
    }

    [Fact]
    public async Task ExistingEfConvertersSupplyAccessorInputsIncludingNullsAndStructsWithoutEqualityOperators()
    {
        await using var context = Context();
        await Seed(context);
        var builder = Builder();
        var entity = builder.Entity<AdapterRow>();
        entity.HasAccessor(row => row.Code).UseValueConverter<string>();
        entity.HasAccessor(row => row.Alias).UseValueConverter<string>();
        var model = Model(context, builder);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var repository = Activator.CreateInstance(TypeOf(assembly, "AdapterRowRepository"), context)!;
        (await Invoke<Task<IReadOnlyList<AdapterRow>>>(Invoke<object>(repository, "ByCode", "A"), "AsEntitiesAsync", CancellationToken.None)).Should().ContainSingle().Which.Id.Should().Be(1);
        (await Invoke<Task<IReadOnlyList<AdapterRow>>>(Invoke<object>(repository, "ByAlias", (object?)null), "AsEntitiesAsync", CancellationToken.None)).Should().ContainSingle().Which.Id.Should().Be(2);
        (await Invoke<Task<IReadOnlyList<AdapterRow>>>(Invoke<object>(repository, "ByAlias", "UPPER"), "AsEntitiesAsync", CancellationToken.None)).Should().ContainSingle().Which.Id.Should().Be(1);
        model.Entities.Single().Lookups.Single(lookup => lookup.MethodName == "ByAlias").Properties.Single().TypeName.Should().Be("global::System.String?");
    }

    [Fact]
    public async Task StronglyTypedEfPrimaryKeyCanReuseItsProviderConverter()
    {
        await using var context = Context();
        await Seed(context);
        var builder = Builder();
        builder.Entity<AdapterConvertedRow>().HasAccessor(row => row.Id).UseValueConverter<int>();
        var model = Model(context, builder);
        model.Entities.Single().Lookups.Single().IsUnique.Should().BeTrue();
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var repository = Activator.CreateInstance(TypeOf(assembly, "AdapterConvertedRowRepository"), context)!;
        (await Invoke<Task<AdapterConvertedRow?>>(Invoke<object>(repository, "ById", 4), "AsEntityAsync", CancellationToken.None))!.Name.Should().Be("converted");
    }

    [Fact]
    public async Task ReusableModelMappingBindsClosedPortsAndMapsSavedGeneratedValues()
    {
        await using var context = Context();
        await Seed(context);
        var builder = Builder(closed: true);
        builder.Configure(options => options.IncludeApiDataContracts());
        var id = builder.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        var dto = builder.HasTypeAdapter<AdapterRow, AdapterRowDto>(
            row => new AdapterRowDto(id.ToContract(row.Id), row.Name, row.Scope),
            value => new AdapterRow { Id = id.ToModel(value.Id), Name = value.Name, Scope = value.Scope });
        var create = builder.HasTypeAdapter<AdapterRow, AdapterCreate>(
            row => new AdapterCreate(row.Name, row.Scope), value => new AdapterRow { Name = value.Name, Scope = value.Scope });
        var entity = builder.Entity<AdapterRow>();
        entity.BindReadPort<IAdapterReadPort>();
        entity.BindWritePort<IAdapterWritePort>();
        entity.HasProjection(dto);
        entity.HasQuery("Find").Where<ApplicationRowId>((row, rowId) => row.Id == id.ToModel(rowId)).ProjectTo<AdapterRowDto>().SingleOrDefault();
        entity.HasQuery("Find").Where<int>((row, rowId) => row.Id == rowId).ProjectTo<AdapterRowDto>().SingleOrDefault();
        entity.HasQuery("All").OrderBy(row => row.Id).ProjectTo<AdapterRowDto>().List();
        entity.HasCommand("Create").Create<AdapterCreate>(request => create.ToModel(request)).Save().Returns(row => dto.ToContract(row));
        entity.HasCommand("Rename").Where<AdapterRename>((row, request) => row.Id == id.ToModel(request.Id))
            .Patch(setters => setters.SetProperty<string, AdapterRename>(row => row.Name, (row, request) => request.Name))
            .Save().Returns(row => dto.ToContract(row));
        var model = Model(context, builder);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var operations = Activator.CreateInstance(TypeOf(assembly, "AdapterRowRepositoryOperations"), context)!;
        var read = (IAdapterReadPort)operations;
        var write = (IAdapterWritePort)operations;
        (await read.FindAsync(new ApplicationRowId(1)))!.Name.Should().Be("one");
        (await read.FindAsync(1))!.Id.Should().Be(new ApplicationRowId(1));
        (await read.FindAsync(new ApplicationRowId(999))).Should().BeNull();
        (await read.AllAsync()).Select(row => row.Id.Value).Should().Equal(1, 2);
        var saved = await write.CreateAsync(new AdapterCreate("created", 8));
        saved.Status.Should().Be(CommandStatus.Applied);
        saved.Value!.Id.Value.Should().BeGreaterThan(2);
        var renamed = await write.RenameAsync(new AdapterRename(saved.Value.Id, "renamed"));
        renamed.Value!.Name.Should().Be("renamed");
        (await read.FindAsync(saved.Value.Id))!.Name.Should().Be("renamed");
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().NotContain("EntityFrameworkCore").And.NotContain("RepositoryTypeAdapter").And.NotContain("AdapterRow>");
        var sources = RepositorySourceRenderer.Render(model);
        sources.Should().NotContain("RepositoryTypeAdapter").And.NotContain(".ToContract(").And.NotContain(".ToModel(");
        var snapshot = RepositoryGenerator.Generate(context, builder.Build()).CreateApiSnapshot();
        snapshot.ToJson().Should().Contain("ApplicationRowId").And.Contain("AdapterRowDto").And.NotContain("RepositoryTypeAdapter");
    }

    [Fact]
    public async Task MappingsExpandInsideFromFragmentsGroupsCursorsAndWriteConditions()
    {
        await using var context = Context();
        await Seed(context);
        var builder = Builder(closed: true);
        var id = builder.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        var dto = builder.HasTypeAdapter<AdapterRow, AdapterRowDto>(row => new AdapterRowDto(id.ToContract(row.Id), row.Name, row.Scope),
            value => new AdapterRow { Id = id.ToModel(value.Id), Name = value.Name, Scope = value.Scope });
        var entity = builder.Entity<AdapterRow>();
        var fragment = entity.HasFragment<ApplicationRowId>("ById", (row, rowId) => row.Id == id.ToModel(rowId));
        entity.HasQuery("From").From<ApplicationRowId, AdapterRowDto>((sources, rowId) => sources.Set<AdapterRow>()
            .Where(row => row.Id == id.ToModel(rowId)).Select(row => dto.ToContract(row))).List();
        entity.HasQuery("Grouped").GroupBy(row => row.Scope, group => new AdapterGroup(id.ToContract(group.Key), group.Count())).List();
        entity.HasQuery("Cursor").OrderBy(row => row.Id).ProjectTo(row => dto.ToContract(row))
            .CursorPaged<ApplicationRowId>((row, cursor) => row.Id > id.ToModel(cursor), row => id.ToContract(row.Id));
        entity.HasCommand("Erase").Use(fragment).TrackedDelete().Save();
        var model = Model(context, builder);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var operations = Activator.CreateInstance(TypeOf(assembly, "AdapterRowRepositoryOperations"), context)!;
        (await Invoke<Task<IReadOnlyList<AdapterRowDto>>>(operations, "FromAsync", new ApplicationRowId(2), CancellationToken.None)).Should().ContainSingle().Which.Id.Value.Should().Be(2);
        (await Invoke<Task<IReadOnlyList<AdapterGroup>>>(operations, "GroupedAsync", CancellationToken.None)).Should().ContainSingle().Which.Count.Should().Be(2);
        var page = await Invoke<Task<CursorPagedResult<AdapterRowDto, ApplicationRowId>>>(operations, "CursorAsync", new CursorPageRequest<ApplicationRowId>(1), CancellationToken.None);
        page.HasMore.Should().BeTrue();
        page.NextCursor.Should().Be(new ApplicationRowId(1));
        (await Invoke<Task<CommandResult>>(operations, "EraseAsync", new ApplicationRowId(2), CancellationToken.None)).Status.Should().Be(CommandStatus.Applied);
        (await context.Rows.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void ModelAdaptersExposeNavigationLoadingAndDependencyInventoryBeforeRendering()
    {
        using var context = Context();
        var builder = Builder(closed: true);
        var dto = builder.HasTypeAdapter<AdapterRow, AdapterOwnerDto>(row => new AdapterOwnerDto(row.Id, row.Owner == null ? null : row.Owner.Name),
            value => new AdapterRow { Id = value.Id });
        var entity = builder.Entity<AdapterRow>();
        entity.HasProjection(dto);
        entity.HasQuery("Owners").ProjectTo<AdapterOwnerDto>().List();
        var model = Model(context, builder);
        var query = model.Entities.Single().Queries.Single();
        query.Includes.Should().ContainSingle().Which.ExpressionSource.Should().Contain("entity.Owner");
        query.Cache.DependentEntityTypeNames.Should().Contain(RepositoryModelFactory.CSharpTypeName(typeof(AdapterOwner)));
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
    }

    [Fact]
    public void InputAnnotationsAreAppliedToEveryAffectedAccessorAndCanBeCleared()
    {
        using var context = Context();
        var builder = Builder();
        var id = builder.HasTypeAdapter<int, AdapterKey<string>>(value => new AdapterKey<string>(null!, value), value => value.Number);
        var accessor = builder.Entity<AdapterRow>().HasAccessor(row => row.Id).UseAdapter(id)
            .InputNullability<AdapterKey<string>>(type => type.TypeArgument(0, part => part.Nullable()));
        var first = Model(context, builder);
        first.Entities.Single().Lookups.SelectMany(lookup => lookup.Properties).Where(property => property.PropertyName == "Id")
            .Should().OnlyContain(property => property.TypeName.EndsWith("<global::System.String?>", StringComparison.Ordinal));
        ClosedRepositoryTests.Compile(first, rejectWarnings: true);
        accessor.ClearInputMapping();
        var cleared = Model(context, builder).Entities.Single();
        cleared.Lookups.Single(lookup => lookup.MethodName == "ById").IsUnique.Should().BeTrue();
        cleared.Lookups.SelectMany(lookup => lookup.Properties).Where(property => property.PropertyName == "Id")
            .Should().OnlyContain(property => property.TypeName == "global::System.Int32" && property.ComparisonSource == null);
    }

    [Fact]
    public void AdapterDefinitionsAndNestedCallsAreReusableAndBuildIsIdempotent()
    {
        using var context = Context();
        var builder = Builder(closed: true);
        var id = builder.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        builder.Entity<AdapterRow>().HasQuery("Value").Select(row => id.ToContract(id.ToModel(id.ToContract(row.Id)))).List();
        var first = RepositoryGenerator.Generate(context, builder.Build());
        var second = RepositoryGenerator.Generate(context, builder.Build());
        second.Files.Should().Equal(first.Files);
        builder.Build().TypeAdapters.Should().ContainSingle().Which.ToModel.Body.Should().BeAssignableTo<MemberExpression>();
        var action = () => id.ToModel(new ApplicationRowId(1));
        action.Should().Throw<InvalidOperationException>().WithMessage("RGD1701:*");
    }

    [Theory]
    [InlineData("foreignExpression", "RGD1702")]
    [InlineData("foreignAccessor", "RGD1702")]
    [InlineData("propertyGetter", "RGD1701")]
    [InlineData("recursive", "RGD1703")]
    [InlineData("wrongProperty", "RGD1704")]
    [InlineData("duplicateMapping", "RGD1704")]
    [InlineData("noConverter", "RGD1704")]
    [InlineData("wrongConverter", "RGD1704")]
    [InlineData("annotationMismatch", "RGN0002")]
    [InlineData("annotationDuplicate", "RGN0001")]
    public void InvalidAdaptersHaveFailFastDiagnosticsWithoutInvokingGetters(string scenario, string diagnostic)
    {
        using var context = Context();
        var builder = Builder();
        var other = new RepositoryModelBuilder();
        var owner = scenario.StartsWith("foreign", StringComparison.Ordinal) ? other : builder;
        RepositoryTypeAdapter<int, ApplicationRowId> id = null!;
        id = scenario == "recursive"
            ? owner.HasTypeAdapter<int, ApplicationRowId>(value => id.ToContract(value), value => value.Value)
            : owner.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        var entity = builder.Entity<AdapterRow>();
        var action = () =>
        {
            switch (scenario)
            {
                case "foreignExpression": entity.HasQuery("Bad").Select(row => id.ToContract(row.Id)).List(); break;
                case "foreignAccessor": entity.HasAccessor(row => row.Id).UseAdapter(id); break;
                case "propertyGetter":
                    var holder = new ThrowingAdapterHolder();
                    entity.HasQuery("Bad").Select(row => holder.Adapter.ToContract(row.Id)).List();
                    break;
                case "recursive": entity.HasQuery("Bad").Select(row => id.ToContract(row.Id)).List(); break;
                case "wrongProperty": entity.HasAccessor(row => row.Name).UseAdapter(id); break;
                case "duplicateMapping": entity.HasAccessor(row => row.Id).UseAdapter(id).UseValueConverter<int>(); break;
                case "noConverter": entity.HasAccessor(row => row.Id).UseValueConverter<int>(); break;
                case "wrongConverter": entity.HasAccessor(row => row.Code).UseValueConverter<int>(); break;
                case "annotationMismatch": entity.HasAccessor(row => row.Id).UseAdapter(id).InputNullability<string>(type => type.Nullable()); break;
                case "annotationDuplicate": entity.HasAccessor(row => row.Id).InputNullability<int>(type => type.Nullable(false)).InputNullability<int>(type => type.Nullable(false)); break;
            }

            Model(context, builder);
        };
        action.Should().Throw<Exception>().WithMessage(diagnostic + ":*");
    }

    [Fact]
    public void OrdinaryCapturedStateIsNotEnabledByTheAdapterEscapeHatch()
    {
        using var context = Context();
        var builder = Builder();
        var suffix = "configuration state";
        var dto = builder.HasTypeAdapter<AdapterRow, AdapterRowDto>(row => new AdapterRowDto(new ApplicationRowId(row.Id), row.Name + suffix, row.Scope),
            value => new AdapterRow { Name = value.Name });
        builder.Entity<AdapterRow>().HasProjection(dto);
        var action = () => Model(context, builder);
        action.Should().Throw<InvalidOperationException>().WithMessage("*captures configuration-time state*");
    }

    [Theory]
    [InlineData(false, RepositoryOutputLayout.SingleFile)]
    [InlineData(false, RepositoryOutputLayout.PerEntity)]
    [InlineData(true, RepositoryOutputLayout.SingleFile)]
    [InlineData(true, RepositoryOutputLayout.PerEntity)]
    [InlineData(true, RepositoryOutputLayout.PerContract)]
    public void AdapterGenerationIsDeterministicAcrossLayouts(bool closed, RepositoryOutputLayout layout)
    {
        using var context = Context();
        var builder = Builder(closed: closed);
        var id = builder.HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        var entity = builder.Entity<AdapterRow>();
        entity.HasAccessor(row => row.Id).UseAdapter(id);
        entity.HasQuery("Ids").Select(row => id.ToContract(row.Id)).List();
        var first = RepositoryGenerator.Generate(context, builder.Build(), layout);
        first.Files.Should().Equal(RepositoryGenerator.Generate(context, builder.Build(), layout).Files);
        first.Files.Where(file => file.Kind != RepositoryOutputKind.Manifest).Should().OnlyContain(file => !file.Contents.Contains("RepositoryTypeAdapter", StringComparison.Ordinal));
        ClosedRepositoryTests.Compile(Model(context, builder), rejectWarnings: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledReadCapabilitiesDoNotExpandUnusedForeignQueryAdapters(bool cqrs)
    {
        using var context = Context();
        var builder = Builder(closed: !cqrs, cqrs: cqrs);
        var foreign = new RepositoryModelBuilder().HasTypeAdapter<int, ApplicationRowId>(value => new ApplicationRowId(value), value => value.Value);
        var entity = builder.Entity<AdapterRow>();
        entity.ReadEnabled = false;
        entity.HasQuery("Unused").Select(row => foreign.ToContract(row.Id)).List();
        Model(context, builder).Entities.Single().Queries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("key")]
    [InlineData("graph")]
    public void CreateModelAdaptersDoNotBypassProtectedAssignmentsOrGraphWriteValidation(string scenario)
    {
        using var context = Context();
        var builder = Builder(closed: true);
        var mapping = scenario == "key"
            ? builder.HasTypeAdapter<AdapterRow, AdapterCreate>(row => new AdapterCreate(row.Name, row.Scope),
                value => new AdapterRow { Id = value.Scope, Name = value.Name })
            : builder.HasTypeAdapter<AdapterRow, AdapterCreate>(row => new AdapterCreate(row.Name, row.Scope),
                value => new AdapterRow { Name = value.Name, Owner = new AdapterOwner { Name = value.Name } });
        builder.Entity<AdapterRow>().HasCommand("Create").Create<AdapterCreate>(request => mapping.ToModel(request)).Save();
        var action = () => Model(context, builder);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task AdapterPredicatesStayServerTranslatedAndRespectVisibilityPolicies()
    {
        await using var context = Context();
        await Seed(context);
        var builder = Builder(closed: true);
        var number = builder.HasTypeAdapter<int, long>(value => (long)value, value => (int)value);
        var entity = builder.Entity<AdapterRow>();
        entity.HasVisibilityPolicy("OnlyFirst", row => number.ToContract(row.Id) < 2L);
        entity.HasQuery("Scopes").WhereIf<long>(scopeValue => number.ToModel(scopeValue) > 0,
                (row, scopeValue) => row.Scope == number.ToModel(scopeValue))
            .OrderBy(row => number.ToContract(row.Id)).Select(row => number.ToContract(row.Id)).List();
        var assembly = ClosedRepositoryTests.Compile(Model(context, builder), rejectWarnings: true);
        var operations = Activator.CreateInstance(TypeOf(assembly, "AdapterRowRepositoryOperations"), context)!;
        (await Invoke<Task<IReadOnlyList<long>>>(operations, "ScopesAsync", 7L, CancellationToken.None)).Should().Equal(1L);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableReferenceAnnotationsCannotHideDuplicateAccessorAndFilterSignatures(bool cqrs)
    {
        using var context = Context();
        var builder = Builder(cqrs: cqrs);
        var id = builder.HasTypeAdapter<int, AdapterKey<string>>(value => new AdapterKey<string>("", value), value => value.Number);
        var entity = builder.Entity<AdapterRow>();
        entity.HasAccessor(row => row.Id).UseAdapter(id)
            .InputNullability<AdapterKey<string>>(type => type.TypeArgument(0, part => part.Nullable()));
        entity.HasFilter<AdapterKey<string>>("ById", (row, rowId) => row.Id == rowId.Number);
        var action = () => Model(context, builder);
        action.Should().Throw<InvalidOperationException>().WithMessage("RGD1705:*");
    }

    [Fact]
    public void NullableValueTypesRemainDistinctAccessorOverloads()
    {
        using var context = Context();
        var builder = Builder(cqrs: true);
        var id = builder.HasTypeAdapter<int, int?>(value => value, value => value ?? 0);
        var entity = builder.Entity<AdapterRow>();
        entity.HasAccessor(row => row.Id).UseAdapter(id);
        entity.HasFilter<int>("ById", (row, rowId) => row.Id == rowId);
        ClosedRepositoryTests.Compile(Model(context, builder), rejectWarnings: true);
    }

    private sealed class ThrowingAdapterHolder
    {
        private readonly string message = "Getter must not run!";
        public RepositoryTypeAdapter<int, ApplicationRowId> Adapter => throw new InvalidOperationException(message);
    }

    internal static AdapterContext Context() => new(new DbContextOptionsBuilder<AdapterContext>().UseSqlite("Data Source=:memory:").Options);

    internal static RepositoryModelBuilder Builder(bool closed = false, bool cqrs = false)
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (closed)
        {
            builder.Configure(options => options.UseClosedRepositories("Adapters.Contracts"));
        }

        if (cqrs)
        {
            builder.Configure(options => options.UseCqrsSelectors());
        }

        return builder;
    }

    internal static RepositoryGenerationModel Model(AdapterContext context, RepositoryModelBuilder builder)
        => RepositoryModelFactory.Create(context, typeof(AdapterContext).Assembly, true, builder.Build());

    internal static async Task Seed(AdapterContext context)
    {
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Rows.AddRange(new AdapterRow { Id = 1, Name = "one", Scope = 7, Code = new StorageCode("A"), Alias = "upper" },
            new AdapterRow { Id = 2, Name = "two", Scope = 7, Code = new StorageCode("B") });
        context.Links.Add(new AdapterLink { Scope = 7, Number = 2, Label = "link" });
        context.Converted.Add(new AdapterConvertedRow { Id = new StorageNumber(4), Name = "converted" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    internal static Type TypeOf(Assembly assembly, string name) => assembly.GetType(typeof(AdapterRow).Namespace + "." + name, throwOnError: true)!;

    internal static T Invoke<T>(object target, string method, params object?[] arguments)
    {
        if (target.GetType().GetMethod(method) is { } instance)
        {
            return (T)instance.Invoke(target, arguments)!;
        }

        var extensions = target.GetType().Assembly.GetType(target.GetType().FullName + "Extensions")!;
        return (T)extensions.GetMethod(method)!.Invoke(null, new object?[] { target }.Concat(arguments).ToArray())!;
    }
}

public readonly record struct ApplicationRowId(int Value);

public sealed record AdapterRowDto(ApplicationRowId Id, string Name, int Scope);

public sealed record AdapterCreate(string Name, int Scope);

public sealed record AdapterRename(ApplicationRowId Id, string Name);

public sealed record AdapterGroup(ApplicationRowId Scope, int Count);

public sealed record AdapterOwnerDto(int Id, string? OwnerName);

public sealed record AdapterKey<T>(T Part, int Number);

public interface IAdapterReadPort
{
    Task<AdapterRowDto?> FindAsync(ApplicationRowId rowId, CancellationToken cancellationToken = default);
    Task<AdapterRowDto?> FindAsync(int rowId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdapterRowDto>> AllAsync(CancellationToken cancellationToken = default);
}

public interface IAdapterWritePort
{
    Task<CommandResult<AdapterRowDto>> CreateAsync(AdapterCreate request, CancellationToken cancellationToken = default);
    Task<CommandResult<AdapterRowDto>> RenameAsync(AdapterRename request, CancellationToken cancellationToken = default);
}

public readonly struct StorageCode(string value)
{
    public string Value { get; } = value;
}

public readonly record struct StorageNumber(int Value);

public sealed class AdapterRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Scope { get; set; }
    public StorageCode Code { get; set; } = new("");
    public string? Alias { get; set; }
    public int? OwnerId { get; set; }
    public AdapterOwner? Owner { get; set; }
}

public sealed class AdapterLink
{
    public int Scope { get; set; }
    public int Number { get; set; }
    public string Label { get; set; } = "";
}

public sealed class AdapterConvertedRow
{
    public StorageNumber Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class AdapterOwner
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class AdapterContext(DbContextOptions<AdapterContext> options) : DbContext(options)
{
    public DbSet<AdapterRow> Rows => Set<AdapterRow>();
    public DbSet<AdapterLink> Links => Set<AdapterLink>();
    public DbSet<AdapterConvertedRow> Converted => Set<AdapterConvertedRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdapterRow>().HasIndex(row => new { row.Scope, row.Id });
        modelBuilder.Entity<AdapterRow>().Property(row => row.Code).HasConversion(value => value.Value, value => new StorageCode(value));
        modelBuilder.Entity<AdapterRow>().Property(row => row.Alias).HasConversion(value => value == null ? null : value.ToUpperInvariant(), value => value == null ? null : value.ToLowerInvariant());
        modelBuilder.Entity<AdapterRow>().HasOne(row => row.Owner).WithMany().HasForeignKey(row => row.OwnerId);
        modelBuilder.Entity<AdapterLink>().HasKey(row => new { row.Scope, row.Number });
        modelBuilder.Entity<AdapterConvertedRow>().Property(row => row.Id).HasConversion(new ValueConverter<StorageNumber, int>(value => value.Value, value => new StorageNumber(value)));
        modelBuilder.Entity<AdapterConvertedRow>().Property(row => row.Id).ValueGeneratedNever();
    }
}