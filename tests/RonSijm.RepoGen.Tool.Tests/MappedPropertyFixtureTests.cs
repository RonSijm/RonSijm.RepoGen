using FluentAssertions;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedPropertyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyFixtureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedClrVersionsRemainPortableInBothExplicitFixtureShapes(bool mapped)
    {
        var builder = ImmediateTestHarness.Builder(mapped);
        builder.Entity<ImmediateRow>().HasCommand("MappedPatch").Where<ImmediateRequest>((row, input) => row.Id == input.Delta)
            .Patch(setters => setters.SetProperty(row => row.Amount, row => row.Amount + 1).SetProperty(row => row.Version, row => row.Version + 1))
            .AllowWrite(row => row.Version).ExpectVersion<int, ImmediateRequest>("Version", input => input.ExpectedVersion).Save();
        var assembly = ImmediateTestHarness.Compile(builder); var (_, repository, _) = ImmediateTestHarness.Fixture(mapped, assembly);
        var wrong = await ImmediateTestHarness.Invoke<CommandResult>(repository, "MappedPatchAsync", new ImmediateRequest(1, Delta: 1, ExpectedVersion: 99));
        wrong.Status.Should().Be(CommandStatus.Conflict);
        var saved = await ImmediateTestHarness.Invoke<CommandResult>(repository, "MappedPatchAsync", new ImmediateRequest(1, Delta: 1));
        saved.Status.Should().Be(CommandStatus.Applied);
        (await ImmediateTestHarness.Invoke<CommandResult>(repository, "MappedPatchAsync", new ImmediateRequest(1, Delta: 1))).Status.Should().Be(CommandStatus.Conflict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStringBindingDoesNotAuthorizeFixtureShadowState(bool acknowledge)
    {
        using var context = Empty(); var builder = Builder().Configure(options => options.UseScriptedTestDoubles(Namespace + ".TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands());
        var entity = builder.Entity<MappedPropertyNote>(); entity.HasInMemoryStore(row => row.Id, row => new MappedPropertyNote { Id = row.Id, Title = row.Title, Counter = row.Counter });
        var command = Versioned(builder, "Rename", tracked: true);
        if (acknowledge) command.RequireTestOverride("The test explicitly owns database-generated shadow token results.");
        if (acknowledge) ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
        else
        {
            Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
            generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*Named concurrency tokens*");
        }
    }
}
