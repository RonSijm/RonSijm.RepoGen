using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyCollisionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyntheticSlotsCannotSupplyAnUnassignedClrScalarOrRelationshipKey(bool relationship)
    {
        using var context = new MappedKeyCollisionContext(new DbContextOptionsBuilder<MappedKeyCollisionContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Generated.Collision.Contracts").UseGeneratedNamespace("Generated.Collision.Persistence")
            .UseScriptedTestDoubles("Generated.Collision.Testing").UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries());
        var owner = builder.Entity<MappedKeyCollisionRow>();
        owner.WriteEnabled = false;
        owner.HasInMemoryStore<MappedKeyCollisionSeed, int>(row => row.Id, row => new(row.Id, row.Rank))
            .MapInMemoryEntity<MappedKeyCollisionSeed>(row => new MappedKeyCollisionRow { Id = row.Id })
            .MapInMemoryProperty<MappedKeyCollisionSeed, int>("Rank", row => row.Rank);
        owner.HasQuery("Rank").Select(row => EF.Property<int>(row, "Rank")).List();
        var safe = RepositoryGenerator.Generate(context, builder.Build());
        ScriptedTestDoubleTestHarness.Compile(safe);
        safe.Files.Single(file => file.Kind == RepositoryOutputKind.TestDoubles).Contents.Should().Contain("__repoGenProperty0_");
        if (relationship)
        {
            var child = builder.Entity<MappedKeyCollisionChild>();
            child.WriteEnabled = false;
            child.HasInMemoryStore<MappedKeyCollisionSeed, int>(row => row.Id, row => new(row.Id, row.Rank))
                .MapInMemoryEntity<MappedKeyCollisionSeed>(row => new MappedKeyCollisionChild { Id = row.Id })
                .MapInMemoryProperty<MappedKeyCollisionSeed, int?>("OwnerId", row => row.Rank);
            owner.HasInMemoryNavigation(row => row.Children, useMappedPropertyKeys: true);
            owner.HasQuery("Read").Select(row => row.Children.Count).List();
        }
        else
        {
            owner.HasQuery("Read").Select(row => row.__repoGenProperty0).List();
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3302:*");
    }
}
