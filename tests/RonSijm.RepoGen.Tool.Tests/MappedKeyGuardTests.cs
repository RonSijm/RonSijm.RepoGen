using FluentAssertions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedKeyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyGuardTests
{
    [Theory]
    [InlineData("NoPermission")]
    [InlineData("MissingPrincipal")]
    [InlineData("MissingForeign")]
    [InlineData("MissingJoin")]
    [InlineData("MissingInverse")]
    [InlineData("MissingFilter")]
    [InlineData("UnsafeOptionalTarget")]
    public void KeyPermissionDoesNotInferBindingsOtherEdgesFiltersOrNullSemantics(string mode)
    {
        using var context = Context();
        var builder = Base(permission: mode != "NoPermission");
        var owner = builder.Entity<MappedKeyOwner>();
        switch (mode)
        {
            case "MissingPrincipal":
                owner.WithoutInMemoryProperty("Scope");
                break;
            case "MissingForeign":
                builder.Entity<MappedKeyChild>().WithoutInMemoryProperty("OwnerCode");
                break;
            case "MissingJoin":
                builder.Entity<MappedKeyLink>().WithoutInMemoryProperty("TagId");
                break;
            case "MissingInverse":
                builder.Entity<MappedKeyTag>().HasInMemoryNavigation(row => row.Owners);
                break;
            case "MissingFilter":
                builder.Entity<MappedKeyLink>().UseInMemoryModelQueryFilter(false);
                break;
        }
        Action acknowledge;
        if (mode == "MissingInverse")
        {
            var query = builder.Entity<MappedKeyTag>().HasQuery("Read").Select(row => row.Owners.Where(item => item != null).Select(item => item.Name).ToList()).List();
            acknowledge = () => query.RequireTestOverride("This relationship deliberately stays scripted.");
        }
        else if (mode == "MissingForeign")
        {
            var query = owner.HasQuery("Read").Select(row => row.Children.Select(item => item.Name).ToList()).List();
            acknowledge = () => query.RequireTestOverride("This relationship deliberately stays scripted.");
        }
        else if (mode == "UnsafeOptionalTarget")
        {
            var query = owner.HasQuery("Read").Select(row => row.Tags.Count).List();
            acknowledge = () => query.RequireTestOverride("This relationship deliberately stays scripted.");
        }
        else
        {
            var query = owner.HasQuery("Read").Select(row => row.Tags.Where(item => item != null).Select(item => item.Name).ToList()).List();
            acknowledge = () => query.RequireTestOverride("This relationship deliberately stays scripted.");
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
        acknowledge();
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    [Fact]
    public void RelationshipReadPermissionDoesNotEnableWriteStateOrOriginalVersions()
    {
        using var context = Context();
        var builder = Base();
        builder.Configure(options => options.UseInMemoryTestCommands().RequirePortableTestCommands());
        var owner = builder.Entity<MappedKeyOwner>();
        owner.WriteEnabled = true;
        owner.HasCommand("Rename").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Name, "changed")).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
    }
}
