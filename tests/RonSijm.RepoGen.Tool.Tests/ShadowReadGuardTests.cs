using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ShadowReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ShadowReadGuardTests
{
    [Theory]
    [InlineData("unbound")]
    [InlineData("widen")]
    [InlineData("box")]
    [InlineData("dynamic")]
    [InlineData("identity")]
    [InlineData("objectReceiver")]
    [InlineData("constructedReceiver")]
    [InlineData("unsafeReference")]
    public void UnsupportedReadsRemainOverrideBoundariesRatherThanDefaultValues(string mode)
    {
        using var context = Context();
        var builder = Base(filters: false);
        var query = builder.Entity<ShadowReadNote>().HasQuery("Read").IgnoreQueryFilters();
        switch (mode)
        {
            case "unbound":
                builder.Entity<ShadowReadNote>().WithoutInMemoryProperty("Rank");
                query.Select(row => EF.Property<int>(row, "Rank")).List();
                break;
            case "widen":
                query.Select(row => EF.Property<long>(row, "Rank")).List();
                break;
            case "box":
                query.Select(row => EF.Property<object>(row, "Rank")).List();
                break;
            case "dynamic":
                query.Select<int, string>((row, name) => EF.Property<int>(row, name)).List();
                break;
            case "identity":
                query.Where(row => row.Equals(row)).Select(row => row.Id).List();
                break;
            case "objectReceiver":
                query.Select(row => EF.Property<int>((object)row, "Rank")).List();
                break;
            case "constructedReceiver":
                query.Select(row => EF.Property<int>(new ShadowReadNote { Id = row.Id }, "Rank")).List();
                break;
            default:
                query.Select(row => EF.Property<bool>(row.Folder!, "Archived")).List();
                builder.Entity<ShadowReadFolder>().UseInMemoryModelQueryFilter(includeNavigations: true);
                // IgnoreQueryFilters would suppress this target's filter, so use the normal root query instead.
                builder.Entity<ShadowReadNote>().ExcludeQuery("Read");
                query = builder.Entity<ShadowReadNote>().HasQuery("Read");
                query.Select(row => EF.Property<bool>(row.Folder!, "Archived")).List();
                builder.Entity<ShadowReadNote>().UseInMemoryModelQueryFilter(includeNavigations: true);
                break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
        query.RequireTestOverride("This operation deliberately uses a typed fixture handler.");
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void ShadowReadPermissionDoesNotEnableModelFiltersOrTrackedShadowWrites()
    {
        using var context = Context();
        var builder = Base(filters: false);
        var note = builder.Entity<ShadowReadNote>();
        var query = note.HasQuery("Read");
        query.Select(row => row.Id).List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*Active EF model query filters*");
        query.IgnoreQueryFilters();
        builder.Configure(options => options.UseInMemoryTestCommands().RequirePortableTestCommands());
        note.WriteEnabled = true;
        note.HasCommand("Rename").Where<int>((row, id) => row.Id == id)
            .Patch(setters => setters.SetProperty(row => row.Title, "changed")).Save();
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }

    [Fact]
    public void EachStoreMustBindItsOwnPropertyEvenWhenAnotherEntityUsesTheSameName()
    {
        using var context = Context();
        var builder = Base();
        builder.Entity<ShadowReadFolder>().HasQuery("Read").Select(row => EF.Property<int>(row, "Rank")).List();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
        builder.Entity<ShadowReadFolder>().MapInMemoryProperty<ShadowReadFolderSeed, int>("Rank", row => row.Id);
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }
}
