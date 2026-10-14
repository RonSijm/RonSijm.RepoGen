using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.CompositeCollectionTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionBoundaryTests
{
    [Theory]
    [InlineData("duplicate")]
    [InlineData("computed")]
    [InlineData("foreignKey")]
    [InlineData("converted")]
    public void ComponentsMustBeDistinctDirectNonForeignKeyScalars(string scenario)
    {
        using var context = Context();
        var builder = Minimal();
        var command = builder.Entity<CompositeCollectionParent>().HasCommand("Change").Where<int>((row, id) => row.Id == id);
        Expression<Func<CompositeCollectionChild, (string, string)>> key = scenario switch
        {
            "duplicate" => row => new ValueTuple<string, string>(row.Code, row.Code),
            "computed" => row => new ValueTuple<string, string>(row.Code, row.Language.ToUpperInvariant()),
            _ => row => new ValueTuple<string, string>(row.Code, row.Language)
        };
        if (scenario == "foreignKey") command.ReconcileCollection<CompositeCollectionChild, CompositeCollectionItem, (string, int), CompositeCollectionRequest>(
            row => row.Children, request => request.Items!, row => new ValueTuple<string, int>(row.Code, row.TenantId),
            item => new ValueTuple<string, int>(item.Code, item.Id), children => children.MaximumItems(5));
        else if (scenario == "converted") command.ReconcileCollection<CompositeCollectionChild, CompositeCollectionItem, (object, string), CompositeCollectionRequest>(
            row => row.Children, request => request.Items!, row => new ValueTuple<object, string>((object)row.Code, row.Language),
            item => new ValueTuple<object, string>(item.Code, item.Language), children => children.MaximumItems(5));
        else command.ReconcileCollection<CompositeCollectionChild, CompositeCollectionItem, (string, string), CompositeCollectionRequest>(
            row => row.Children, request => request.Items!, key, item => new ValueTuple<string, string>(item.Code, item.Language), children => children.MaximumItems(5));
        command.Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2801:*");
    }

    [Theory]
    [InlineData("Code")]
    [InlineData("Language")]
    public void EveryMatchingComponentIsImmutableEvenWithExplicitWritePermission(string property)
    {
        using var context = Context();
        var builder = Minimal();
        builder.Entity<CompositeCollectionParent>().HasCommand("Change").Where<int>((row, id) => row.Id == id)
            .ReconcileCollection<CompositeCollectionChild, CompositeCollectionItem, (string, string), CompositeCollectionRequest>(
                row => row.Children, request => request.Items!, row => new ValueTuple<string, string>(row.Code, row.Language), item => new ValueTuple<string, string>(item.Code, item.Language),
                children =>
                {
                    children.MaximumItems(5).AllowWrite(row => row.Code).AllowWrite(row => row.Language);
                    if (property == "Code") children.Update(setters => setters.SetProperty(row => row.Code, "changed"));
                    else children.Update(setters => setters.SetProperty(row => row.Language, "changed"));
                }).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2802:*matching keys*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddMustExplicitlySupplyEachMatchingComponent(bool omitFirst)
    {
        using var context = Context();
        var builder = Minimal();
        builder.Entity<CompositeCollectionParent>().HasCommand("Change").Where<int>((row, id) => row.Id == id)
            .ReconcileCollection<CompositeCollectionChild, CompositeCollectionItem, (string, string), CompositeCollectionRequest>(
                row => row.Children, request => request.Items!, row => new ValueTuple<string, string>(row.Code, row.Language), item => new ValueTuple<string, string>(item.Code, item.Language),
                children =>
                {
                    children.MaximumItems(5);
                    if (omitFirst) children.Add(item => new CompositeCollectionChild { Language = item.Language });
                    else children.Add(item => new CompositeCollectionChild { Code = item.Code });
                }).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2802:*every matching key component*");
    }

    [Fact]
    public void ArbitraryKeyConstructorsAndNestedTuplesAreNotAssumedToHaveMatchingSemantics()
    {
        using var context = Context();
        var builder = Minimal();
        builder.Entity<CompositeCollectionParent>().HasCommand("Change").Where<int>((row, id) => row.Id == id)
            .ReconcileCollection<CompositeCollectionChild, CompositeCollectionItem, CompositeCollectionCustomKey, CompositeCollectionRequest>(
                row => row.Children, request => request.Items!, row => new CompositeCollectionCustomKey(row.Code, row.Language),
                item => new CompositeCollectionCustomKey(item.Code, item.Language), children => children.MaximumItems(5)).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2801:*explicit ValueTuple construction*");
        builder = Minimal();
        builder.Entity<CompositeCollectionParent>().HasCommand("Change").Where<int>((row, id) => row.Id == id)
            .ReconcileCollection<CompositeCollectionChild, CompositeCollectionItem, ((string, string), string), CompositeCollectionRequest>(
                row => row.Children, request => request.Items!, row => new ValueTuple<ValueTuple<string, string>, string>(new(row.Code, row.Language), row.Text),
                item => new ValueTuple<ValueTuple<string, string>, string>(new(item.Code, item.Language), item.Text), children => children.MaximumItems(5)).Save();
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2801:*direct mapped scalar*");
    }

    [Fact]
    public void AScalarConverterDoesNotSmuggleANestedTuplePastComponentValidation()
    {
        using var context = new CompositeCollectionNestedContext(new DbContextOptionsBuilder<CompositeCollectionNestedContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = Minimal();
        builder.Entity<CompositeCollectionWideOwner>().HasCommand("Change").Where<int>((row, id) => row.Id == id)
            .ReconcileCollection<CompositeCollectionWideChild, CompositeCollectionWideInput, ((string, string), int), CompositeCollectionWideRequest>(
                row => row.Children, request => request.Items, row => new ValueTuple<ValueTuple<string, string>, int>(row.PackedKey, row.Key1),
                item => new ValueTuple<ValueTuple<string, string>, int>(new("code", "locale"), item.Key1), children => children.MaximumItems(5)).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2801:*nested ValueTuple components*");
    }

    [Fact]
    public void RequiredColumnsWithNullableClrTypesCannotHideNestedTuples()
    {
        using var context = new CompositeCollectionNullableNestedContext(new DbContextOptionsBuilder<CompositeCollectionNullableNestedContext>().UseSqlite("Data Source=:memory:").Options);
        context.Model.FindEntityType(typeof(CompositeCollectionWideChild))!.FindProperty(nameof(CompositeCollectionWideChild.NullablePackedKey))!
            .IsNullable.Should().BeFalse();
        var builder = Minimal();
        builder.Entity<CompositeCollectionWideOwner>().HasCommand("Change").Where<int>((row, id) => row.Id == id)
            .ReconcileCollection<CompositeCollectionWideChild, CompositeCollectionWideInput, (ValueTuple<string, string>?, int), CompositeCollectionWideRequest>(
                row => row.Children, request => request.Items, row => new ValueTuple<ValueTuple<string, string>?, int>(row.NullablePackedKey, row.Key1),
                item => new ValueTuple<ValueTuple<string, string>?, int>(new ValueTuple<string, string>("code", "locale"), item.Key1), children => children.MaximumItems(5)).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2801:*nested ValueTuple components*");
    }

    private static RepositoryModelBuilder Minimal() => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories(Namespace + ".Contracts").GenerateOnlyConfiguredEntities());
}
