using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Testing;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningMappedFixtureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StrictFixturesStillNeedExplicitOverrideAcknowledgements(bool acknowledge)
    {
        var builder = ImmediateTestHarness.Builder();
        var command = builder.Entity<ImmediateRow>().HasCommand("MappedBump").Update(setters => setters.SetProperty(row => row.Amount, row => row.Amount + 1))
            .ReturnsMany(row => row.Amount).IncludeMappedProperties().SimulateImmediateWrites();
        if (acknowledge) command.RequireTestOverride("Native mapped values are supplied by this test explicitly.");
        if (acknowledge) ImmediateTestHarness.Compile(builder);
        else
        {
            Action compile = () => ImmediateTestHarness.Compile(builder);
            compile.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*IncludeMappedProperties*");
        }
    }

    [Fact]
    public async Task UnarrangedNonStrictFixturesDoNotReturnFalseSuccessForMappedCapture()
    {
        var builder = ImmediateTestHarness.Builder(strict: false);
        builder.Entity<ImmediateRow>().HasCommand("MappedBump").Update(setters => setters.SetProperty(row => row.Amount, row => row.Amount + 1))
            .ReturnsMany(row => row.Amount).IncludeMappedProperties().SimulateImmediateWrites();
        var assembly = ImmediateTestHarness.Compile(builder); var (_, repository, _) = ImmediateTestHarness.Fixture(false, assembly);
        Func<Task> action = () => ImmediateTestHarness.Invoke<MutationResult<int>>(repository, "MappedBumpAsync");
        await action.Should().ThrowAsync<UnsupportedInMemoryRepositoryOperationException>();
    }

    [Fact]
    public void DisablingBroadCaptureRetainsIndependentlyConfiguredShadowPermission()
    {
        using var context = ReturningShadowTestHarness.Empty();
        var builder = ReturningMappedDesignTests.Minimal();
        builder.Entity<ReturningShadowRow>().HasCommand("Remove").Delete().ReturnsMany(row => EF.Property<int>(row, "Credit"))
            .IncludeShadowProperties().IncludeMappedProperties().IncludeMappedProperties(false);
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents))
            .Should().Contain("ExecuteWithStateAsync").And.NotContain("ExecuteWithMappedStateAsync");
    }
}
