using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedReconciliationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReconciliationAdapterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredTypeAdaptersExpandInsideChildScalarRecipes(bool condition)
    {
        await using var context = Context();
        await SeedAsync(context);
        var builder = Builder();
        var number = builder.HasTypeAdapter<int, long>(value => (long)value, value => (int)value);
        builder.Entity<MappedCollectionParent>().HasCommand("Adapt").Where<MappedReconciliationRequest>((row, request) => row.Id == request.Id)
            .ReconcileCollection<MappedCollectionChild, MappedCollectionItem, string, MappedReconciliationRequest>(row => row.Children, request => request.Items,
                row => row.Code, item => item.Code, children =>
                {
                    children.MaximumItems(3).Update(setters => setters.SetPropertyWhen<int>("audit",
                        (child, item) => number.ToModel((long)item.Revision + 100), item => number.ToModel((long)item.Revision) > 0));
                }).Save().SimulateCollectionWrites().ReturnsSaved(row => EF.Property<int>(row, "Revision"));
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        var production = NativeSavedTestHarness.CompileProduction(context, builder);
        var (memory, _, data) = Fixture(ScriptedTestDoubleTestHarness.Compile(bundle));
        var item = Request().Items[0] with { Revision = condition ? 3 : 0 };
        var request = Request() with { Items = [item] };
        var actual = await Call<CommandResult<int>>(Operations(context, assembly: production), "Adapt", request);
        (await Call<CommandResult<int>>(memory, "Adapt", request)).Should().BeEquivalentTo(actual);
        actual.Status.Should().Be(condition ? CommandStatus.Applied : CommandStatus.NoChanges);
        ChildStore(data).Find(1)!.Audit.Should().Be(condition ? 103 : 10);
    }
}
