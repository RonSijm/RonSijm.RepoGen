using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContractNullabilityTests
{
    [Fact]
    public async Task NestedInputsAndResultsBindAndExecuteIncludingCachedReadsAndSavedNullValues()
    {
        await using var context = Context();
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        context.Rows.AddRange(new CommandRow { Id = 1, OwnerId = 1, Name = "one", Description = "description" },
            new CommandRow { Id = 2, OwnerId = 1, Name = "two" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = Builder();
        builder.Configure(options => options.UseClosedCaching("nullable-contract-tests"));
        var entity = builder.Entity<CommandRow>();
        entity.BindReadPort<INullabilityReadPort>();
        entity.BindWritePort<INullabilityWritePort>();
        entity.HasQuery("Find").Cache().InputNullability<IReadOnlyList<string>>("names", type => type.Nullable().TypeArgument(0, item => item.Nullable()))
            .Where<IReadOnlyList<string?>>((row, names) => names == null || names.Contains(row.Name))
            .OrderBy(row => row.Id).Select(row => row.Description).ResultNullability<string>(type => type.Nullable()).List();
        entity.HasQuery("Find").InputNullability<string[]>("names", type => type.Nullable().Element(item => item.Nullable()))
            .Where<string?[]>((row, names) => names == null || Enumerable.Contains(names, row.Name))
            .OrderBy(row => row.Id).Select(row => row.Description).ResultNullability<string>(type => type.Nullable()).List();
        entity.HasQuery("Values").Where<int?[]>((row, values) => Enumerable.Contains(values, (int?)row.Id)).Count();
        entity.HasQuery("Dictionary").Select(row => row).Dictionary(row => row.Id, row => row.Description).ResultNullability<string>(type => type.Nullable());
        entity.HasQuery("Lookup").Select(row => row).Lookup(row => row.OwnerId, row => row.Description).ResultNullability<string>(type => type.Nullable());
        entity.HasQuery("Page").OrderBy(row => row.Id).Select(row => row.Description).ResultNullability<string>(type => type.Nullable()).Paged();
        entity.HasQuery("Slice").OrderBy(row => row.Id).Select(row => row.Description).ResultNullability<string>(type => type.Nullable()).Slice();
        entity.HasQuery("Stream").OrderBy(row => row.Id).Select(row => row.Description).ResultNullability<string>(type => type.Nullable()).Stream();
        entity.HasQuery("First").Where<int>((row, id) => row.Id == id).Select(row => row.Description).ResultNullability<string>(type => type.Nullable()).FirstOrDefault();
        entity.HasQuery("Bucket").Where<int>((row, id) => row.Id == id).Select(row => NullabilityMapping.Bucket(row.Description))
            .ResultNullability<IReadOnlyList<string>>(type => type.Nullable().TypeArgument(0, item => item.Nullable())).Single();
        entity.HasQuery("Maximum").Select(row => row).Max(row => row.Description).ResultNullability<string>(type => type.Nullable());
        entity.HasQuery("Boxes").Select(row => new NullabilityBox<string?> { Id = row.Id, Value = row.Description }).OrderBy(row => row.Id)
            .ResultNullability<NullabilityBox<string>>(type => type.TypeArgument(0, item => item.Nullable())).List();
        entity.HasQuery("BoxPage").Select(row => new NullabilityBox<string?> { Id = row.Id, Value = row.Description }).OrderBy(row => row.Id)
            .ResultNullability<NullabilityBox<string>>(type => type.TypeArgument(0, item => item.Nullable())).Paged();
        entity.HasQuery("After").Select(row => new NullabilityBox<string?> { Id = row.Id, Value = row.Description }).OrderBy(row => row.Id)
            .ResultNullability<NullabilityBox<string>>(type => type.TypeArgument(0, item => item.Nullable()))
            .CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
        entity.HasCommand("SetText").Where<int>((row, id) => row.Id == id)
            .InputNullability<IReadOnlyList<string>>("values", type => type.Nullable().TypeArgument(0, item => item.Nullable()))
            .Patch(setters => setters.SetProperty<string?, IReadOnlyList<string?>>(row => row.Description, (row, values) => NullabilityMapping.FirstValue(values)))
            .Save().Returns(row => row.Description).ResultNullability<string>(type => type.Nullable());
        entity.HasCommand("SaveBox").Where<int>((row, id) => row.Id == id).AllowNullInput<string>("text")
            .Patch(setters => setters.SetProperty<string?, string?>(row => row.Description, (row, text) => text))
            .Save().Returns(row => new NullabilityBox<string?> { Id = row.Id, Value = row.Description })
            .ResultNullability<NullabilityBox<string>>(type => type.TypeArgument(0, item => item.Nullable()));
        var model = Model(context, builder);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var operations = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, context, new MemoryRepositoryResultCache(memory))!;
        var read = (INullabilityReadPort)operations;
        var write = (INullabilityWritePort)operations;
        (await read.FindAsync((IReadOnlyList<string?>?)null)).Should().Equal("description", null);
        (await read.FindAsync((IReadOnlyList<string?>?)null)).Should().Equal("description", null);
        (await read.FindAsync(new List<string?> { null, "two" })).Should().ContainSingle().Which.Should().BeNull();
        (await read.FindAsync(new string?[] { null, "one" })).Should().Equal("description");
        (await read.FindAsync(Array.Empty<string?>())).Should().BeEmpty();
        (await read.ValuesAsync([null, 1])).Should().Be(1);
        (await read.DictionaryAsync())[2].Should().BeNull();
        (await read.LookupAsync())[1].Should().Equal("description", null);
        var page = await read.PageAsync(1, 2);
        page.TotalCount.Should().Be(2);
        page.Items.Should().Equal("description", null);
        (await read.SliceAsync(1, 1)).HasMore.Should().BeTrue();
        var streamed = new List<string?>();
        await foreach (var value in read.StreamAsync())
        {
            streamed.Add(value);
        }

        streamed.Should().Equal("description", null);
        (await read.FirstAsync(999)).Should().BeNull();
        (await read.FirstAsync(2)).Should().BeNull();
        (await read.BucketAsync(1)).Should().Equal("description", null);
        (await read.BucketAsync(2)).Should().BeNull();
        (await read.MaximumAsync()).Should().Be("description");
        (await read.BoxesAsync()).Select(box => box.Value).Should().Equal("description", null);
        (await read.BoxPageAsync(1, 2)).Items[1].Value.Should().BeNull();
        var after = await read.AfterAsync(new CursorPageRequest<int>(1));
        after.HasMore.Should().BeTrue();
        after.NextCursor.Should().Be(1);
        var saved = await write.SetTextAsync(1, [null]);
        saved.Status.Should().Be(CommandStatus.Applied);
        saved.HasValue.Should().BeTrue();
        saved.Value.Should().BeNull();
        (await read.FindAsync((string?[]?)null)).Should().Equal(new string?[] { null, null });
        (await read.FindAsync((IReadOnlyList<string?>?)null)).Should().Equal(new string?[] { null, null });
        var savedBox = await write.SaveBoxAsync(1, null);
        savedBox.Status.Should().Be(CommandStatus.Applied);
        savedBox.Value!.Value.Should().BeNull();
        savedBox = await write.SaveBoxAsync(1, "boxed");
        savedBox.HasValue.Should().BeTrue();
        savedBox.Value!.Value.Should().Be("boxed");
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().NotContain("IQueryable").And.NotContain("Microsoft.EntityFrameworkCore");
        model.Entities.Single().Queries.Where(query => query.MethodName == "FindAsync").Select(RepositoryOperationSignature.Query)
            .Should().OnlyContain(signature => !signature.Contains('?'));
        model.Entities.Single().Queries.Single(query => query.MethodName == "ValuesAsync").Parameters[0].TypeName.Should().Contain("Int32?[]");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedProjectionsInheritAnnotationsAndTypedSourceAndGroupRootsCompileAndExecute(bool closed)
    {
        await using var context = Context();
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        context.Rows.Add(new CommandRow { OwnerId = 1, Name = "nullable" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (closed)
        {
            builder.Configure(options => options.UseClosedRepositories("Nullability.Contracts"));
        }

        builder.Entity<CommandRow>(entity =>
        {
            entity.HasProjection("ProjectToBox", row => new NullabilityBox<string?> { Id = row.Id, Value = row.Description })
                .ResultNullability(type => type.TypeArgument(0, item => item.Nullable())).AutoIncludeNavigations(false);
            entity.HasQuery("Named").ProjectTo("ProjectToBox").List();
            entity.HasQuery("Source").From(sources => sources.Set<CommandRow>().Select(row => new NullabilityBox<string?> { Id = row.Id, Value = row.Description }))
                .ResultNullability<NullabilityBox<string>>(type => type.TypeArgument(0, item => item.Nullable())).List();
            entity.HasQuery("Group").GroupBy(row => row.OwnerId, group => new NullabilityBox<string?> { Id = group.Key, Value = group.Max(row => row.Description) })
                .ResultNullability<NullabilityBox<string>>(type => type.TypeArgument(0, item => item.Nullable()));
        });
        var model = Model(context, builder);
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var ns = typeof(CommandRow).Namespace;
        var target = Activator.CreateInstance(assembly.GetType(ns + (closed ? ".CommandRowRepositoryOperations" : ".CommandRowRepository"))!, context)!;
        foreach (var name in new[] { "NamedAsync", "SourceAsync", "GroupAsync" })
        {
            var task = (Task<IReadOnlyList<NullabilityBox<string?>>>)(closed
                ? target.GetType().GetMethod(name)!.Invoke(target, [CancellationToken.None])!
                : assembly.GetType(ns + ".CommandRowConfiguredQueryExtensions")!.GetMethod(name)!.Invoke(null, [target, CancellationToken.None])!);
            (await task).Should().ContainSingle().Which.Value.Should().BeNull();
        }

        model.Entities.Single().Queries.Single(query => query.MethodName == "NamedAsync").ResultTypeName.Should().EndWith("<global::System.String?>");
    }

    [Theory]
    [InlineData("rectangular", "global::System.String?[,]?")]
    [InlineData("jagged", "global::System.String?[]?[]")]
    [InlineData("mapping", "global::System.Collections.Generic.IReadOnlyDictionary<global::System.String, global::System.Collections.Generic.IReadOnlyList<global::System.String?>?>?")]
    [InlineData("value", "global::RonSijm.RepoGen.Tool.Tests.NullabilityValue<global::System.String?>?")]
    [InlineData("nested", "global::RonSijm.RepoGen.Tool.Tests.NullabilityOuter<global::System.String?>.Inner<global::System.Collections.Generic.IReadOnlyList<global::System.String?>?>?")]
    [InlineData("leaf", "global::RonSijm.RepoGen.Tool.Tests.NullabilityOuter<global::System.String?>.Leaf")]
    public void TypeShapesMatchReflectedPortAnnotationsAndRetainClrRanksAndNesting(string parameter, string expected)
    {
        var method = typeof(INullabilityShapes).GetMethod(nameof(INullabilityShapes.Inputs))!;
        var info = method.GetParameters().Single(input => input.Name == parameter);
        var builder = Builder();
        var query = builder.Entity<CommandRow>().HasQuery("Shapes");
        switch (parameter)
        {
            case "rectangular": query.InputNullability<string[,]>("value", type => type.Nullable().Element(item => item.Nullable())); break;
            case "jagged": query.InputNullability<string[][]>("value", type => type.Element(array => array.Nullable().Element(item => item.Nullable()))); break;
            case "mapping": query.InputNullability<IReadOnlyDictionary<string, IReadOnlyList<string>>>("value", type => type.Nullable().TypeArgument(1, list => list.Nullable().TypeArgument(0, item => item.Nullable()))); break;
            case "value": query.InputNullability<NullabilityValue<string>?>("value", type => type.TypeArgument(0, value => value.TypeArgument(0, item => item.Nullable()))); break;
            case "nested": query.InputNullability<NullabilityOuter<string>.Inner<IReadOnlyList<string>>>("value", type => type.Nullable().TypeArgument(0, item => item.Nullable()).TypeArgument(1, list => list.Nullable().TypeArgument(0, item => item.Nullable()))); break;
            case "leaf": query.InputNullability<NullabilityOuter<string>.Leaf>("value", type => type.TypeArgument(0, item => item.Nullable())); break;
        }

        var shape = builder.Build().Entities.Single().Queries.Single().InputNullability.Single().Nullability;
        RepositoryTypeName.Annotated(shape).Should().Be(expected);
        RepositoryPortBinding.TypeName(new NullabilityInfoContext().Create(info)).Should().Be(expected);
        RepositoryTypeName.Clr(info.ParameterType).Should().Be(RepositoryModelFactory.CSharpTypeName(shape.Type));
    }

    [Theory]
    [InlineData("arrayElementOnScalar")]
    [InlineData("argumentOnScalar")]
    [InlineData("negativeArgument")]
    [InlineData("outOfRange")]
    [InlineData("nullableValue")]
    public void InvalidAnnotationPathsFailAtDesignTime(string scenario)
    {
        var query = Builder().Entity<CommandRow>().HasQuery("Invalid");
        Action configure = () =>
        {
            switch (scenario)
            {
                case "arrayElementOnScalar": query.InputNullability<string>("input", type => type.Element(item => item.Nullable())); break;
                case "argumentOnScalar": query.InputNullability<string>("input", type => type.TypeArgument(0, item => item.Nullable())); break;
                case "negativeArgument": query.InputNullability<List<string>>("input", type => type.TypeArgument(-1, item => item.Nullable())); break;
                case "outOfRange": query.InputNullability<List<string>>("input", type => type.TypeArgument(1, item => item.Nullable())); break;
                case "nullableValue": query.InputNullability<int>("input", type => type.Nullable()); break;
            }
        };
        configure.Should().Throw<ArgumentException>().WithMessage("RGN0003:*");
    }

    [Theory]
    [InlineData("missing", "RGN0002")]
    [InlineData("wrongInput", "RGN0002")]
    [InlineData("duplicateInput", "RGN0001")]
    [InlineData("duplicateOverload", "Duplicate closed operation")]
    [InlineData("wrongResult", "RGN0004")]
    [InlineData("changedResult", "RGN0004")]
    [InlineData("noReturns", "RGN0004")]
    public void InconsistentDeclarationsFailBeforeRendering(string scenario, string diagnostic)
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<CommandRow>();
        var query = entity.HasQuery("Find").Where<string[]>((row, names) => names.Contains(row.Name)).Select(row => row.Description).List();
        switch (scenario)
        {
            case "missing": query.InputNullability<string[]>("absent", type => type.Element(item => item.Nullable())); break;
            case "wrongInput": query.InputNullability<List<string>>("names", type => type.TypeArgument(0, item => item.Nullable())); break;
            case "duplicateInput": query.AllowNullInput<string[]>("names").InputNullability<string[]>("names", type => type.Element(item => item.Nullable())); break;
            case "duplicateOverload":
                entity.HasQuery("Find").InputNullability<string[]>("names", type => type.Element(item => item.Nullable()))
                    .Where<string[]>((row, names) => names.Contains(row.Name)).Select(row => row.Description).List(); break;
            case "wrongResult": query.ResultNullability<int>(type => type.Nullable(false)); break;
            case "changedResult": query.ResultNullability<string>(type => type.Nullable()).Select(value => value!.Length).List(); break;
            case "noReturns": entity.HasCommand("Delete").Where<int>((row, id) => row.Id == id).Delete().ResultNullability<string>(type => type.Nullable()); break;
        }

        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("*" + diagnostic + "*");
    }

    [Fact]
    public void AnnotationBuildersAreSnapshottedAndDuplicateResultsAndStaleBuildersAreRejected()
    {
        var builder = Builder();
        RepositoryTypeNullabilityBuilder? captured = null;
        var query = builder.Entity<CommandRow>().HasQuery("Find").InputNullability<string[]>("names", type =>
            {
                captured = type;
                type.Element(item => item.Nullable());
            })
            .Where<string[]>((row, names) => names.Contains(row.Name)).Select(row => row.Description);
        captured!.Nullable();
        builder.Build().Entities.Single().Queries.Single().InputNullability.Single().Nullability.NullableReference.Should().BeFalse();
        query.ResultNullability<string>(type => type.Nullable());
        Action duplicate = () => query.ResultNullability<string>(type => type.Nullable());
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("RGN0005:*");
        query.Select(value => value!.Length).List();
        Action stale = () => query.InputNullability<string[]>("names", type => type.Nullable());
        stale.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NullableChangesAreApiChangesNotClrOverloadsAndDefaultsStillRejectMismatchedPorts()
    {
        using var context = Context();

        RepositoryModelBuilder Create(bool annotated)
        {
            var builder = Builder();
            var entity = builder.Entity<CommandRow>();
            entity.WriteEnabled = false;
            var query = entity.HasQuery("Find").Where<string[]>((row, names) => names.Contains(row.Name)).Select(row => row.Description).List();
            if (annotated)
            {
                query.InputNullability<string[]>("names", type => type.Element(item => item.Nullable())).ResultNullability<string>(type => type.Nullable());
            }

            return builder;
        }

        var baseline = RepositoryGenerator.Generate(context, Create(false).Build()).CreateApiSnapshot();
        var candidate = RepositoryGenerator.Generate(context, Create(true).Build()).CreateApiSnapshot();
        candidate.CompareTo(baseline).Should().NotBeEmpty().And.OnlyContain(difference => difference.DiagnosticId == "RGA1004" && difference.IsBreaking);
        candidate.Contracts.Single(contract => contract.Type.Contains("IReadCommandRowRepository", StringComparison.Ordinal)).Methods.Single().Signature.Should().Be("FindAsync(global::System.String[])");
        var invalid = Create(false);
        invalid.Entity<CommandRow>().BindReadPort<INullabilityArrayPort>();
        Action bind = () => Model(context, invalid);
        bind.Should().Throw<InvalidOperationException>().WithMessage("*incompatible signature or nullability*");
    }

    [Fact]
    public void LinqInferenceRetainsExplicitClrWideningAndTypeOnlyGenericArguments()
    {
        Expression<Func<string[], bool>> contains = values => Enumerable.Contains(values, "text");
        ProjectionExpressionRenderer.Render(contains).Should().Contain(".Contains(entity, \"text\")");
        Expression<Func<string[], IEnumerable<object>>> widen = values => Enumerable.AsEnumerable<object>(values);
        ProjectionExpressionRenderer.Render(widen).Should().Contain(".AsEnumerable<global::System.Object>");
        Expression<Func<string[], IEnumerable<object>>> typeOnly = values => values.Cast<object>();
        ProjectionExpressionRenderer.Render(typeOnly).Should().Contain(".Cast<global::System.Object>");
    }

    private static RepositoryModelBuilder Builder()
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Nullability.Contracts").GenerateOnlyConfiguredEntities());
        return builder;
    }

    private static CommandContext Context() => new(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
    private static RepositoryGenerationModel Model(CommandContext context, RepositoryModelBuilder builder) => RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, true, builder.Build());
}

public interface INullabilityArrayPort
{
    Task<IReadOnlyList<string?>> FindAsync(string?[] names, CancellationToken cancellationToken = default);
}

public interface INullabilityReadPort
{
    Task<IReadOnlyList<string?>> FindAsync(IReadOnlyList<string?>? names, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string?>> FindAsync(string?[]? names, CancellationToken cancellationToken = default);
    Task<int> ValuesAsync(int?[] values, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, string?>> DictionaryAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, IReadOnlyList<string?>>> LookupAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<string?>> PageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<SliceResult<string?>> SliceAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string?> StreamAsync(CancellationToken cancellationToken = default);
    Task<string?> FirstAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string?>?> BucketAsync(int id, CancellationToken cancellationToken = default);
    Task<string?> MaximumAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NullabilityBox<string?>>> BoxesAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<NullabilityBox<string?>>> BoxPageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<NullabilityBox<string?>, int>> AfterAsync(CursorPageRequest<int> cursorPage, CancellationToken cancellationToken = default);
}

public interface INullabilityWritePort
{
    Task<CommandResult<string?>> SetTextAsync(int id, IReadOnlyList<string?>? values, CancellationToken cancellationToken = default);
    Task<CommandResult<NullabilityBox<string?>>> SaveBoxAsync(int id, string? text, CancellationToken cancellationToken = default);
}

public interface INullabilityShapes
{
    void Inputs(string?[,]? rectangular, string?[]?[] jagged, IReadOnlyDictionary<string, IReadOnlyList<string?>?>? mapping,
        NullabilityValue<string?>? value, NullabilityOuter<string?>.Inner<IReadOnlyList<string?>?>? nested, NullabilityOuter<string?>.Leaf leaf);
}

public readonly record struct NullabilityValue<T>(T Value);

public sealed class NullabilityOuter<T>
{
    public sealed record Inner<TInner>(T Value, TInner InnerValue);

    public sealed record Leaf(T Value);
}

public sealed class NullabilityBox<T>
{
    public int Id { get; init; }
    public T Value { get; init; } = default!;
}

public static class NullabilityMapping
{
    public static IReadOnlyList<string?>? Bucket(string? value) => value is null ? null : [value, null];
    public static string? FirstValue(IReadOnlyList<string?>? values) => values is { Count: > 0 } ? values[0] : null;
}