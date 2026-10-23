using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ContextFilterTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContextFilterGuardTests
{
    [Fact]
    public void RegistrationsAreImmutableSnapshotsAndDuplicateSymbolsAreRejected()
    {
        var builder = Base(bound: false); var before = builder.Build(); Bind(builder);
        before.InMemoryModelFilterContexts.Should().BeEmpty(); builder.Build().InMemoryModelFilterContexts.Should().HaveCount(3);
        Action duplicate = () => builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, string>(context => context.Tenant, key => key.Tenant);
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("RGD4701:*");
        Action captured = () => builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, string>(context => context.RuntimeKey.Tenant, key => key.Tenant);
        captured.Should().Throw<InvalidOperationException>().WithMessage("RGD4701:*");
        Action converted = () => builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, object>(context => context.FolderId, key => key.FolderId);
        converted.Should().Throw<InvalidOperationException>().WithMessage("RGD4701:*");
        Action missingSource = () => builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, string>(null!, key => key.Tenant);
        missingSource.Should().Throw<ArgumentNullException>();
        Action missingKey = () => builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, string>(context => context.Tenant, null!);
        missingKey.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("clock")]
    [InlineData("helper")]
    [InlineData("graph")]
    [InlineData("identity")]
    public void UnsafeKeySelectorsAreRejectedWithoutExecutingThem(string mode)
    {
        using var context = Context(); var builder = Base(bound: false);
        var captured = "never read this captured value";
        Expression<Func<ContextFilterKey, string>> replacement = mode switch
        {
            "capture" => key => key.Tenant + captured,
            "clock" => key => DateTime.Now.Hour > 0 ? key.Tenant : string.Empty,
            "helper" => key => ModelFilterGuardCalls.Accept(key.Tenant).ToString(),
            "graph" => key => new ContextFilterRequest(key).Key.Tenant,
            _ => key => key.ToString()
        };
        builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, string>(value => value.Tenant, replacement);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4702:*");
    }

    [Fact]
    public void BindingRequiresTheConfiguredTypedContextKeyAndCorrectContext()
    {
        using var context = Context(); var builder = Base();
        builder.Configure(options => options.UseKeyedContextLeases<string>());
        Action wrongKey = () => RepositoryGenerator.Generate(context, builder.Build()); wrongKey.Should().Throw<InvalidOperationException>().WithMessage("RGD4701:*");
        using var plain = ModelFilterTestHarness.Context();
        Action wrongContext = () => RepositoryGenerator.Generate(plain, Base().Build()); wrongContext.Should().Throw<InvalidOperationException>().WithMessage("RGD4701:*");
    }

    [Fact]
    public void PartialBindingDoesNotEvaluateOrSilentlyIgnoreAnotherContextMember()
    {
        using var context = Context(); var builder = Base(bound: false, strict: false);
        builder.HasInMemoryModelFilterContext<ContextFilterContext, ContextFilterKey, string>(value => value.Tenant, key => key.Tenant);
        builder.Entity<ModelFilterRow>().HasQuery("Read").UseContextKey<ContextFilterRequest, ContextFilterKey>(input => input.Key).Select(row => row.Id).List();
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        model.Entities.Single(entity => entity.EntityName == "ModelFilterRow").Queries.Single().InMemoryQuery!.Supported.Should().BeFalse();
        builder.Configure(options => options.RequirePortableTestQueries());
        Action strict = () => RepositoryGenerator.Generate(context, builder.Build()); strict.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*Captured objects*");
    }

    [Fact]
    public void BindingAloneDoesNotAuthorizeReadWriteOrRelatedDatasetModelFilters()
    {
        using var context = Context(); var builder = Builder(enabled: false, strict: false);
        var model = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: builder.Build());
        var entity = model.Entities.Single(item => item.EntityName == "ModelFilterRow");
        entity.Queries.Single(query => query.MethodName == "CardsAsync").InMemoryQuery!.Supported.Should().BeFalse();
        entity.Commands.Single(command => command.MethodName == "RenameAsync").InMemoryCommand!.Supported.Should().BeFalse();
        var related = Builder(strict: false); related.Entity<ModelFilterFolder>().UseInMemoryModelQueryFilter(false);
        var relatedModel = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: related.Build());
        relatedModel.Entities.Single(item => item.EntityName == "ModelFilterRow").Queries.Single(query => query.MethodName == "JoinedAsync").InMemoryQuery!.Supported.Should().BeFalse();
    }

    [Fact]
    public async Task ScalarContextKeysCanBeBoundDirectlyWithoutAWrapperType()
    {
        using var context = new ModelFilterGuardContext(new DbContextOptionsBuilder<ModelFilterGuardContext>().UseSqlite("Data Source=:memory:").Options, "context");
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence").UseKeyedContextLeases<string>()
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        builder.HasInMemoryModelFilterContext<ModelFilterGuardContext, string, string>(value => value.Tenant, key => key);
        var row = builder.Entity<ModelFilterRow>(); row.WriteEnabled = false;
        row.HasInMemoryStore(item => item.Id, item => new ModelFilterRow { Id = item.Id, Title = item.Title }).UseInMemoryModelQueryFilter();
        row.HasQuery("Read").UseContextKey<string, string>(input => input).Select(item => item.Id).List();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelFilterGuardContextRepositoryFixture")!)!;
        var data = (RonSijm.RepoGen.Testing.InMemoryDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, ["A"])!;
        ModelFilterTestHarness.Store<ModelFilterRow>(data, "ModelFilterRow").SeedRange(Seeds());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelFilterRowRepositoryTestDouble")!, fixture)!;
        (await Call<IReadOnlyList<int>>(repository, "Read", "A")).Should().Equal(1, 3, 4, 5, 6);
    }

    [Fact]
    public async Task PublicInstanceFieldsAreBoundByExactSymbolWithoutReadingTheContextField()
    {
        using var context = new ContextFilterFieldContext(new DbContextOptionsBuilder<ContextFilterFieldContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence").UseKeyedContextLeases<ContextFilterFieldKey>()
            .UseScriptedTestDoubles(Namespace + ".Testing").UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        builder.HasInMemoryModelFilterContext<ContextFilterFieldContext, ContextFilterFieldKey, string>(value => value.Tenant, key => key.Tenant);
        var row = builder.Entity<ModelFilterRow>(); row.WriteEnabled = false;
        row.HasInMemoryStore(item => item.Id, item => new ModelFilterRow { Id = item.Id, Title = item.Title }).UseInMemoryModelQueryFilter();
        row.HasQuery("Read").UseContextKey<ContextFilterFieldKey, ContextFilterFieldKey>(input => input).Select(item => item.Id).List();
        var assembly = ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ContextFilterFieldContextRepositoryFixture")!)!;
        var key = new ContextFilterFieldKey("A");
        var data = (RonSijm.RepoGen.Testing.InMemoryDataContext)fixture.GetType().GetMethod("For")!.Invoke(fixture, [key])!;
        ModelFilterTestHarness.Store<ModelFilterRow>(data, "ModelFilterRow").SeedRange(Seeds());
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Testing.ModelFilterRowRepositoryTestDouble")!, fixture)!;
        (await Call<IReadOnlyList<int>>(repository, "Read", key)).Should().Equal(1, 3, 4, 5, 6);
    }
}
