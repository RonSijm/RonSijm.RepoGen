using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReadGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void ExplicitMappedScalarsCompileInAllLayoutsWithoutEntityOrEfExecution(RepositoryOutputLayout layout)
    {
        using var context = Context();
        MappedReadItem.GetterCalls.Value = 0;
        var design = Builder().Build();
        var bundle = RepositoryGenerator.Generate(context, design, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        SavedResultTestHarness.CompileProduction(context, design);
        MappedReadItem.GetterCalls.Value.Should().Be(0);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        source.Should().Contain("__repoGenProperty").And.NotContain("EntityFrameworkCore").And.NotContain("InMemoryPropertyRead")
            .And.NotContain("new global::RonSijm.RepoGen.Tool.Tests.MappedReadItem").And.NotContain(".ReadAudit(").And.NotContain("get_Item");
        var manifest = bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents;
        manifest.Should().Contain("MapInMemoryProperty").And.Contain("FieldBacked").And.Contain("FieldOnly").And.Contain("Indexer")
            .And.Contain("ClrProperty").And.Contain("ExplicitTypedPropertyValuesNotTrackedState");
        var query = RepositoryModelFactory.Create(context, context.GetType().Assembly, design: design).Entities
            .Single(entity => entity.EntityName == nameof(MappedReadItem)).Queries.Single(query => query.MethodName == "CardsAsync");
        query.InMemoryQuery!.PropertyReads!.Select(read => read.PropertyName).Should().Contain(["Caption", "PublicRank", "audit", "state", "archived", MappedReadContext.Tag]);
    }

    [Fact]
    public void OptInDoesNotChangeProductionApiAndDisabledBackendRemovesAllMappingOutput()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(mappings: false, strict: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Contracts or RepositoryOutputKind.Repositories))
        {
            disabled.Files.Single(other => other.Name == file.Name && other.Kind == file.Kind).Contents.Should().Be(file.Contents);
        }
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
        RepositoryGenerator.Generate(context, Builder(queries: false).Build()).Files.Should()
            .BeEquivalentTo(RepositoryGenerator.Generate(context, Builder(mappings: false, queries: false).Build()).Files);
    }

    [Fact]
    public void DisabledReadsIgnoreNamedBindingsEvenWhenWriteAdaptersRetainOrdinaryScalarHelpers()
    {
        using var context = ProxyWriteTestHarness.Context();
        var builder = ProxyWriteTestHarness.Builder().Configure(options => options.UseInMemoryTestQueries(false));
        ProxyWriteRepositoryDesigner.Configure(builder);
        var ordinaryWrites = RepositoryGenerator.Generate(context, builder.Build());
        // Deliberately conflicts with an initializer assignment: disabled read-only declarations
        // must not be validated, emitted or treated as a write-state override.
        builder.Entity<ProxyWriteRecord>().MapInMemoryProperty<ProxyWriteData, string>(nameof(ProxyWriteRecord.Name), row => row.Caption);
        var disabledReads = RepositoryGenerator.Generate(context, builder.Build());
        disabledReads.Files.Should().BeEquivalentTo(ordinaryWrites.Files);
        ScriptedTestDoubleTestHarness.Compile(disabledReads);
    }

    [Theory]
    [InlineData("Id")]
    [InlineData("Name")]
    public void OneModelScalarCannotHaveBothInitializerAndNamedRecipes(string name)
    {
        using var context = Context();
        var builder = Base();
        var item = builder.Entity<MappedReadItem>();
        if (name == "Id")
        {
            item.MapInMemoryProperty<MappedReadSeed, int>(name, row => row.Id);
        }
        else
        {
            item.MapInMemoryEntity<MappedReadSeed>(row => new MappedReadItem { Id = row.Id, Name = row.Name });
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4901:*both MapInMemoryEntity and MapInMemoryProperty*exactly one*");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("Audit")]
    [InlineData("Folder")]
    public void ExactFinalModelNamesAreRequired(string name)
    {
        using var context = Context();
        var builder = Base();
        builder.Entity<MappedReadItem>().MapInMemoryProperty<MappedReadSeed, int>(name, row => row.Audit);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4901:*mapped scalar*exact*type*");
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("state")]
    [InlineData("fingerprint")]
    [InlineData("indexer")]
    public void ProviderTypesBoxingAndNumericWideningDoNotReplaceExactModelTypes(string mode)
    {
        using var context = Context();
        var builder = Base();
        var item = builder.Entity<MappedReadItem>();
        switch (mode)
        {
            case "audit":
                item.MapInMemoryProperty<MappedReadSeed, long>(mode, row => row.Audit);
                break;
            case "state":
                item.MapInMemoryProperty<MappedReadSeed, string>(mode, row => row.Tag);
                break;
            case "fingerprint":
                item.MapInMemoryProperty<MappedReadSeed, object?>(mode, row => row.Fingerprint);
                break;
            default:
                item.MapInMemoryProperty<MappedReadSeed, int>(MappedReadContext.Tag, row => row.Rank);
                break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4901:*exact*type*");
    }

    [Fact]
    public void ExplicitIndexExpressionAndReadonlyFieldMetadataNeedNoEntityMaterialization()
    {
        using var context = Context();
        var builder = Base();
        var parameter = Expression.Parameter(typeof(MappedReadItem), "row");
        var index = Expression.MakeIndex(parameter, typeof(MappedReadItem).GetProperty("Item")!, [Expression.Constant(MappedReadContext.Tag)]);
        var expression = Expression.Lambda<Func<MappedReadItem, string>>(Expression.Convert(index, typeof(string)), parameter);
        builder.Entity<MappedReadItem>().HasQuery("Read").Select(expression).List();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        ScriptedTestDoubleTestHarness.Compile(bundle);
        SavedResultTestHarness.CompileProduction(context, builder.Build());
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should().Contain("fixedValue").And.Contain("FieldOnly");
    }
}
