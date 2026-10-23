using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReadGuardTests
{
    [Theory]
    [InlineData("unboundMember")]
    [InlineData("dynamicIndexer")]
    [InlineData("wrongIndexerType")]
    [InlineData("unboxedIndexer")]
    [InlineData("behavior")]
    [InlineData("identity")]
    [InlineData("unsafeReference")]
    public void UnboundOrUnsafeExpressionsRemainTypedOverrideBoundaries(string mode)
    {
        using var context = Context();
        var builder = Base();
        var item = builder.Entity<MappedReadItem>();
        var query = item.HasQuery("Read");
        switch (mode)
        {
            case "unboundMember":
                item.WithoutInMemoryProperty(nameof(MappedReadItem.Caption));
                query.Select(row => row.Caption).List();
                break;
            case "dynamicIndexer":
                query.Select<string, string>((row, name) => (string)row[name]!).List();
                break;
            case "wrongIndexerType":
                query.Select(row => (int)row[MappedReadContext.Tag]!).List();
                break;
            case "unboxedIndexer":
                query.Select(row => row[MappedReadContext.Tag]).List();
                break;
            case "behavior":
                query.Select(row => row.ReadAudit()).List();
                break;
            case "identity":
                query.Where(row => row.Equals(row)).Select(row => row.Id).List();
                break;
            default:
                query.Select(row => row.Folder!.Name).List();
                break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
        query.RequireTestOverride("The fixture deliberately scripts this nonportable expression.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void ReadValueBindingsDoNotEnableFieldTokenOrWriteStateSimulation()
    {
        using var context = Context();
        var builder = Base();
        builder.Configure(options => options.UseInMemoryTestCommands().RequirePortableTestCommands());
        var item = builder.Entity<MappedReadItem>();
        item.WriteEnabled = true;
        item.HasCommand("Rename").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Name, "changed"))
            .ExpectVersion<int, int>("audit", id => id).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }
}
