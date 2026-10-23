using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ReturningShadowTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowFixtureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixturesNeverInferShadowStateEvenIfImmediateSimulationIsEnabled(bool simulate)
    {
        using var context = Empty(); var builder = Builder(simulate, strict: true, acknowledge: false);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*fixture shadow state is not simulated*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedCommandsRemainStrictUntilAnExplicitTypedResultIsArranged(bool acknowledge)
    {
        using var context = Empty(); var bundle = RepositoryGenerator.Generate(context, Builder(simulate: true, strict: acknowledge, acknowledge).Build());
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        var fixture = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.ReturningShadowContextRepositoryFixture")!)!;
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".TestDoubles.ReturningShadowRowRepositoryTestDouble")!, fixture)!;
        Func<Task> execute = async () => await ReturningWriteTests.Call<Task<MutationResult<string>>>(repository, "RemoveAsync", CancellationToken.None);
        await execute.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
        ScriptedTestDoubleTestHarness.Returns(repository, "OnRemoveAsync", new MutationResult<string>(["server value supplied by the test"]));
        (await ReturningWriteTests.Call<Task<MutationResult<string>>>(repository, "RemoveAsync", CancellationToken.None)).Items.Should().Equal("server value supplied by the test");
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        // Diagnostic string literals can name unsupported EF calls; executable syntax must not reference them.
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens().Where(token => token.RawKind == (int)SyntaxKind.IdentifierToken).Select(token => token.ValueText)
            .Should().NotContain("EntityFrameworkCore").And.NotContain("EF").And.NotContain("ReturningWriteRow");
    }

    private static RepositoryModelBuilder Builder(bool simulate, bool strict, bool acknowledge)
    {
        var builder = ReturningShadowDesignTests.Minimal().Configure(options => options.UseScriptedTestDoubles(Namespace + ".TestDoubles")
            .UseInMemoryTestStores().UseInMemoryTestCommands().RequirePortableTestCommands(strict));
        var entity = builder.Entity<ReturningShadowRow>();
        entity.HasInMemoryStore(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new ReturningShadowRow
            { TenantId = row.TenantId, Id = row.Id, Text = row.Text, Quantity = row.Quantity, Revision = row.Revision });
        var command = entity.HasCommand("Remove").Delete().ReturnsMany(row => Microsoft.EntityFrameworkCore.EF.Property<string>(row, "Normalized"))
            .IncludeShadowProperties().SimulateImmediateWrites(simulate);
        if (acknowledge) command.RequireTestOverride("The test owns server-generated shadow audit results.");
        return builder;
    }
}
