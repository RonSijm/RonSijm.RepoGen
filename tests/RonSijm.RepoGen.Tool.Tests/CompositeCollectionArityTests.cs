using System.Linq.Expressions;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionArityTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EverySupportedKeyArityCompilesForProductionAndFixtureExecution(int arity)
    {
        using var context = new CompositeCollectionWideContext(new DbContextOptionsBuilder<CompositeCollectionWideContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("Generated.WideCollections.Contracts").UseGeneratedNamespace("Generated.WideCollections.Persistence")
            .UseScriptedTestDoubles("Generated.WideCollections.TestDoubles").UseInMemoryTestStores().UseInMemoryTestCommands()
            .RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        var owner = builder.Entity<CompositeCollectionWideOwner>();
        owner.HasInMemoryStore(row => row.Id, row => new CompositeCollectionWideOwner { Id = row.Id });
        builder.Entity<CompositeCollectionWideChild>().HasInMemoryStore(row => row.Id, row => new CompositeCollectionWideChild
        {
            Id = row.Id, OwnerId = row.OwnerId, Key1 = row.Key1, Key2 = row.Key2, Key3 = row.Key3, Key4 = row.Key4, Key5 = row.Key5, Key6 = row.Key6, Key7 = row.Key7
        });
        var command = owner.HasCommand("Change").Where<CompositeCollectionWideRequest>((row, request) => row.Id == request.Id);
        var definition = arity switch
        {
            2 => typeof(ValueTuple<,>), 3 => typeof(ValueTuple<,,>), 4 => typeof(ValueTuple<,,,>),
            5 => typeof(ValueTuple<,,,,>), 6 => typeof(ValueTuple<,,,,,>), _ => typeof(ValueTuple<,,,,,,>)
        };
        var components = Enumerable.Repeat(typeof(int), arity).ToArray();
        var keyType = definition.MakeGenericType(components);
        LambdaExpression Selector(Type rowType)
        {
            var row = Expression.Parameter(rowType, "row");
            return Expression.Lambda(typeof(Func<,>).MakeGenericType(rowType, keyType), Expression.New(keyType.GetConstructor(components)!,
                Enumerable.Range(1, arity).Select(index => Expression.Property(row, "Key" + index))), row);
        }
        typeof(CompositeCollectionArityTests).GetMethod(nameof(Configure), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(keyType)
            .Invoke(null, [command, Selector(typeof(CompositeCollectionWideChild)), Selector(typeof(CompositeCollectionWideInput))]);
        var design = builder.Build();
        var model = RepositoryModelFactory.Create(context, typeof(CompositeCollectionWideContext).Assembly, design: design);
        model.Entities.Single(entity => entity.EntityName == nameof(CompositeCollectionWideOwner)).Commands.Single().Collections!.Single()
            .MatchingKeyProperties.Should().Equal(Enumerable.Range(1, arity).Select(index => "Key" + index));
        ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, design));
        SavedResultTestHarness.CompileProduction(context, design);
    }

    private static void Configure<TKey>(RepositoryCommandBuilder<CompositeCollectionWideOwner> command,
        Expression<Func<CompositeCollectionWideChild, TKey>> childKey, Expression<Func<CompositeCollectionWideInput, TKey>> itemKey) where TKey : notnull =>
        command.ReconcileCollection<CompositeCollectionWideChild, CompositeCollectionWideInput, TKey, CompositeCollectionWideRequest>(
            row => row.Children, request => request.Items, childKey, itemKey, children => children.MaximumItems(5)
                .MissingItems(RepositoryCollectionRemovalBehavior.Delete)).Save().SimulateCollectionWrites();
}
