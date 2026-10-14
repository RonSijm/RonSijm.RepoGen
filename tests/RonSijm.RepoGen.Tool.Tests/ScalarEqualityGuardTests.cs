using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ScalarEqualityTestHarness;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ScalarEqualityGuardTests
{
    [Theory]
    [InlineData("captured")]
    [InlineData("clock")]
    [InlineData("helper")]
    [InlineData("graph")]
    [InlineData("dropped")]
    public void PoliciesCannotEvaluateCapturedGettersClocksHelpersGraphsOrDropInputs(string kind)
    {
        using var context = ScalarEqualityTestHarness.Context(); var builder = Builder(); var entity = builder.Entity<ScalarEqualityRow>();
        var threshold = 5;
        switch (kind)
        {
            case "captured": entity.HasInMemoryValueEquality(row => row.Amount, (left, right) => left + threshold == right); break;
            case "clock": entity.HasInMemoryValueEquality(row => row.Amount, (left, right) => left + DateTime.UtcNow.Second == right); break;
            case "helper": entity.HasInMemoryValueEquality(row => row.Amount, (left, right) => TranslationCalls.Echo(left) == right); break;
            case "graph": entity.HasInMemoryValueEquality(row => row.Amount, (left, right) => new ScalarEqualityRow { Amount = left }.Amount == right); break;
            case "dropped": entity.HasInMemoryValueEquality(row => row.Amount, (left, right) => left > 0); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build()); generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4102:*");
    }

    [Theory]
    [InlineData("key")]
    [InlineData("foreignKey")]
    [InlineData("uniqueIndex")]
    [InlineData("generated")]
    [InlineData("binaryToken")]
    public void ScalarEqualityCannotOverrideIdentityRelationshipsGeneratedValuesOrBinaryTokenContracts(string kind)
    {
        Action generate = () =>
        {
            if (kind == "key")
            {
                using var context = ScalarEqualityTestHarness.Context(); var builder = Builder(); builder.Entity<ScalarEqualityRow>().HasInMemoryValueEquality(row => row.Id, (left, right) => left == right);
                RepositoryGenerator.Generate(context, builder.Build());
            }
            else if (kind == "binaryToken")
            {
                using var context = ImmediateTestHarness.Context(); var builder = ImmediateTestHarness.Builder();
                builder.Entity<ImmediateRow>().HasInMemoryValueEquality(row => row.Stamp, (left, right) => left.SequenceEqual(right)); RepositoryGenerator.Generate(context, builder.Build());
            }
            else
            {
                using var context = MemoryCollectionTestHarness.Context(); var builder = SavedResultTestHarness.Builder(); var entity = builder.Entity<MemoryCollectionChild>();
                if (kind == "foreignKey") entity.HasInMemoryValueEquality(row => row.ParentId, (left, right) => left == right);
                else if (kind == "uniqueIndex") entity.HasInMemoryValueEquality(row => row.Key, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase));
                else entity.HasInMemoryValueEquality(row => row.Id, (left, right) => left == right);
                RepositoryGenerator.Generate(context, builder.Build());
            }
        };
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD4101:*");
    }

    [Fact]
    public void StoresMustBeDeclaredFirstAndDuplicatesDoNotReplaceExistingPolicies()
    {
        var builder = Builder();
        Action duplicate = () => builder.Entity<ScalarEqualityRow>().HasInMemoryValueEquality(row => row.Label, (left, right) => left == right);
        duplicate.Should().Throw<ArgumentException>().WithMessage("RGD4101:*");
        var empty = new RonSijm.RepoGen.Design.RepositoryModelBuilder().Entity<ScalarEqualityRow>();
        Action missing = () => empty.HasInMemoryValueEquality(row => row.Label, (left, right) => left == right);
        missing.Should().Throw<InvalidOperationException>().WithMessage("RGD4101:*");
    }

    [Fact]
    public void ExplicitPoliciesDoNotAcknowledgeConversionsOrImmediateSqlComparisons()
    {
        using var converted = new ScalarEqualityConvertedContext(new DbContextOptionsBuilder<ScalarEqualityContext>().UseSqlite("Data Source=:memory:").Options);
        Action conversion = () => RepositoryGenerator.Generate(converted, Builder().Build()); conversion.Should().Throw<InvalidOperationException>().WithMessage("*RGD3402*Custom scalar conversions/comparers*");
        using var context = ScalarEqualityTestHarness.Context(); var builder = Builder();
        builder.Entity<ScalarEqualityRow>().HasCommand("Immediate").Where<int>((row, id) => row.Id == id).Delete().SimulateImmediateWrites();
        Action immediate = () => RepositoryGenerator.Generate(context, builder.Build()); immediate.Should().Throw<InvalidOperationException>().WithMessage("*RGD3402*Custom scalar conversions/comparers*");
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("roundTrip")]
    public async Task EqualityDeclarationsCannotHideCaseLosingMappedAdapters(string kind)
    {
        var builder = Builder(mapped: true); var entity = builder.Entity<ScalarEqualityRow>();
        if (kind == "roundTrip") entity.MapInMemoryWrites<ScalarEqualitySeed>((row, saved) => new(row.Id, row.Label == null ? null : row.Label.ToUpperInvariant(), row.Revision, row.Amount, row.Hidden));
        else
        {
            entity.HasInMemoryStore<ScalarEqualitySeed, int>(row => row.Id, row => new(row.Id, row.Label == null ? null : row.Label.ToUpperInvariant(), row.Revision, row.Amount, row.Hidden))
                .MapInMemoryEntity<ScalarEqualitySeed>(row => new ScalarEqualityRow { Id = row.Id, Label = row.Label, Revision = row.Revision, Amount = row.Amount, Hidden = row.Hidden })
                .MapInMemoryWrites<ScalarEqualitySeed>((row, saved) => new(row.Id, row.Label, row.Revision, row.Amount, row.Hidden)); Policies(entity);
        }
        var (repository, data) = Fixture(true, Compile(builder));
        Func<Task> operation = () => Invoke<CommandResult<ScalarEqualitySeed>>(repository, "RenameAsync", new ScalarEqualityRequest(1, "mixed", "v1"));
        await operation.Should().ThrowAsync<InvalidOperationException>().WithMessage("*round-trip*");
        Rows(data, true)[0].Label.Should().Be(kind == "copy" ? "ALPHA" : "Alpha"); data.PendingCount.Should().Be(0);
    }
}
