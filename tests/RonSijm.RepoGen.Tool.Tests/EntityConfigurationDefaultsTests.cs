using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

#pragma warning disable CA1822 // Public instance operation methods are invoked through configuration discovery.

public sealed class EntityConfigurationDefaultsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropertiesOnlyConfigurationNeedsNoConfigureMethodAndStillDiscoversOperations(bool scanAssembly)
    {
        var builder = new RepositoryModelBuilder();
        if (scanAssembly)
        {
            builder.ApplyConfigurationsFromAssembly(typeof(EntityConfigurationDefaultsTests).Assembly, type => type == typeof(PropertiesOnlyConfiguration));
        }
        else
        {
            builder.ApplyConfiguration(new PropertiesOnlyConfiguration());
        }

        var entity = builder.Build().Entities.Should().ContainSingle().Subject;
        entity.ReadEnabled.Should().BeTrue();
        entity.WriteEnabled.Should().BeFalse();
        entity.ProjectionEnabled.Should().BeFalse();
        entity.Queries.Should().ContainSingle().Which.MethodName.Should().Be("Count");
    }

    [Fact]
    public void EmptyConfigurationDefaultsToNullAndRetainsTheEntityDefaults()
    {
        IRepositoryEntityConfiguration<Row> configuration = new EmptyConfiguration();
        configuration.RepositoryEnabled.Should().BeNull();
        configuration.ReadEnabled.Should().BeNull();
        configuration.WriteEnabled.Should().BeNull();
        configuration.ProjectionEnabled.Should().BeNull();

        var builder = new RepositoryModelBuilder().ApplyConfiguration(configuration);
        AssertSettings(builder.Entity<Row>(), true);
    }

    [Fact]
    public void LaterNullSettingsPreserveTheValuesFromEarlierModules()
    {
        var builder = new RepositoryModelBuilder().ApplyConfiguration(new FlagConfiguration(false));
        builder.ApplyConfiguration(new EmptyConfiguration());
        AssertSettings(builder.Entity<Row>(), false);

        builder.ApplyConfiguration(new FlagConfiguration(true)).ApplyConfiguration(new EmptyConfiguration());
        AssertSettings(builder.Entity<Row>(), true);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ExplicitPropertiesOverridePreviouslyConfiguredSettings(bool previous, bool current)
    {
        var builder = new RepositoryModelBuilder().ApplyConfiguration(new FlagConfiguration(previous));
        builder.ApplyConfiguration(new FlagConfiguration(current));

        AssertSettings(builder.Entity<Row>(), current);
    }

    [Fact]
    public void PropertiesApplyBeforeConfigureAndOperationsObserveItsOverrides()
    {
        var builder = new RepositoryModelBuilder().ApplyConfiguration(new CustomConfiguration());

        AssertSettings(builder.Entity<Row>(), true);
        builder.Build().Entities.Should().ContainSingle().Which.Queries.Should().ContainSingle();
    }

    [Fact]
    public void ExplicitInterfacePropertiesApplyOnlyToTheirMatchingEntityContract()
    {
        var builder = new RepositoryModelBuilder().ApplyConfigurationsFromAssembly(typeof(EntityConfigurationDefaultsTests).Assembly,
            type => type == typeof(CombinedConfiguration));

        builder.Entity<Row>().WriteEnabled.Should().BeFalse();
        builder.Entity<OtherRow>().WriteEnabled.Should().BeTrue();
        builder.Build().Entities.Should().HaveCount(2).And.OnlyContain(entity => entity.Queries.Count == 1);
    }

    [Fact]
    public void PropertyGetterFailuresDuringScanningIdentifyTheModuleAndPreserveTheCause()
    {
        var apply = () => new RepositoryModelBuilder().ApplyConfigurationsFromAssembly(typeof(EntityConfigurationDefaultsTests).Assembly,
            type => type == typeof(ThrowingConfiguration));

        apply.Should().Throw<InvalidOperationException>().WithMessage("RGD1006:*ThrowingConfiguration*property failure*")
            .WithInnerException<ArgumentException>().WithMessage("property failure");
    }

    private static void AssertSettings(RepositoryEntityBuilder<Row> entity, bool expected)
    {
        entity.RepositoryEnabled.Should().Be(expected);
        entity.ReadEnabled.Should().Be(expected);
        entity.WriteEnabled.Should().Be(expected);
        entity.ProjectionEnabled.Should().Be(expected);
    }

    private sealed class Row
    {
        public int Id { get; set; }
    }

    private sealed class OtherRow
    {
        public int Id { get; set; }
    }

    private sealed class PropertiesOnlyConfiguration : IRepositoryEntityConfiguration<Row>
    {
        public bool? WriteEnabled => false;
        public bool? ProjectionEnabled => false;

        public RepositoryQueryBuilder<Row> Count(RepositoryEntityBuilder<Row> entity)
        {
            entity.WriteEnabled.Should().BeFalse();
            entity.ProjectionEnabled.Should().BeFalse();
            return entity.HasQuery(nameof(Count)).Count();
        }
    }

    private sealed class EmptyConfiguration : IRepositoryEntityConfiguration<Row>;

    private sealed class FlagConfiguration : IRepositoryEntityConfiguration<Row>
    {
        private readonly bool enabled;

        public FlagConfiguration(bool enabled) => this.enabled = enabled;
        public bool? RepositoryEnabled => enabled;
        public bool? ReadEnabled => enabled;
        public bool? WriteEnabled => enabled;
        public bool? ProjectionEnabled => enabled;
    }

    private sealed class CustomConfiguration : IRepositoryEntityConfiguration<Row>
    {
        public bool? RepositoryEnabled => false;
        public bool? ReadEnabled => false;
        public bool? WriteEnabled => false;
        public bool? ProjectionEnabled => false;

        public void Configure(RepositoryEntityBuilder<Row> entity)
        {
            AssertSettings(entity, false);
            entity.RepositoryEnabled = true;
            entity.ReadEnabled = true;
            entity.WriteEnabled = true;
            entity.ProjectionEnabled = true;
        }

        public RepositoryQueryBuilder<Row> Count(RepositoryEntityBuilder<Row> entity)
        {
            AssertSettings(entity, true);
            return entity.HasQuery(nameof(Count)).Count();
        }
    }

    private sealed class CombinedConfiguration : IRepositoryEntityConfiguration<Row>, IRepositoryEntityConfiguration<OtherRow>
    {
        bool? IRepositoryEntityConfiguration<Row>.WriteEnabled => false;
        bool? IRepositoryEntityConfiguration<OtherRow>.WriteEnabled => true;
        public RepositoryQueryBuilder<Row> Count(RepositoryEntityBuilder<Row> entity) => entity.HasQuery(nameof(Count)).Count();
        public RepositoryQueryBuilder<OtherRow> Count(RepositoryEntityBuilder<OtherRow> entity) => entity.HasQuery(nameof(Count)).Count();
    }

    private sealed class ThrowingConfiguration : IRepositoryEntityConfiguration<Row>
    {
        public bool? WriteEnabled => throw new ArgumentException("property failure");
    }
}
