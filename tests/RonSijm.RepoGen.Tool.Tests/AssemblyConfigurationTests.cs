using System.Reflection;
using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class AssemblyConfigurationTests
{
    [Fact]
    public void DiscoversNonPublicConfigurationsInOrdinalTypeNameOrder()
    {
        var model = CreateModel(typeof(ZConfiguration), typeof(AConfiguration));

        var entity = model.Entities.Should().ContainSingle().Subject;
        entity.Queries.Select(query => query.MethodName).Should().Equal("A", "Z");
        entity.WriteEnabled.Should().BeTrue("later scalar settings override earlier ones");
    }

    [Fact]
    public void SupportsInheritedContractsAndSkipsAbstractAndOpenGenericTypes()
    {
        var model = CreateModel(typeof(GenericConfiguration<>), typeof(AbstractConfiguration), typeof(InheritedConfiguration));

        model.Entities.Should().ContainSingle().Which.Queries.Should().ContainSingle();
    }

    [Fact]
    public void AppliesModelAndMultipleExplicitEntityContractsOnOneInstance()
    {
        var model = CreateModel(typeof(CombinedConfiguration));

        model.Entities.Select(entity => entity.EntityType).Should().BeEquivalentTo([typeof(Row), typeof(OtherRow)]);
        model.Entities.Single(entity => entity.EntityType == typeof(Row)).ReadEnabled.Should().BeTrue();
    }

    [Fact]
    public void PredicateFiltersModulesBeforeConstruction()
    {
        var model = CreateModel(typeof(AConfiguration));

        model.Entities.Should().ContainSingle().Which.Queries.Should().ContainSingle();
    }

    [Fact]
    public void MissingParameterlessConstructorExplainsExplicitRegistrationAlternative()
    {
        var apply = () => CreateModel(typeof(StatefulConfiguration));

        apply.Should().Throw<InvalidOperationException>().WithMessage("RGD1006:*StatefulConfiguration*ApplyConfiguration*");
        var builder = new RepositoryModelBuilder().ApplyConfiguration(new StatefulConfiguration(false));
        builder.Build().Entities.Should().ContainSingle().Which.WriteEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(ThrowingConstructor), "constructor failure")]
    [InlineData(typeof(ThrowingEntityConfiguration), "entity failure")]
    [InlineData(typeof(ThrowingModelConfiguration), "model failure")]
    public void DiscoveryFailuresIdentifyTheModuleAndPreserveTheOriginalException(Type configuration, string message)
    {
        var apply = () => CreateModel(configuration);

        apply.Should().Throw<InvalidOperationException>().WithMessage("RGD1006:*" + configuration.FullName + "*")
            .WithInnerException<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public void NullAssemblyIsRejected()
    {
        var apply = () => new RepositoryModelBuilder().ApplyConfigurationsFromAssembly(null!);

        apply.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }

    [Fact]
    public void TypeLoadFailureIsReportedInsteadOfSilentlyApplyingAPartialAssembly()
    {
        var apply = () => new RepositoryModelBuilder().ApplyConfigurationsFromAssembly(new UnloadableAssembly());

        apply.Should().Throw<InvalidOperationException>().WithMessage("RGD1006:*missing dependency*")
            .WithInnerException<ReflectionTypeLoadException>();
    }

    [Fact]
    public void ExplicitRepeatedScansAccumulateOperations()
    {
        var builder = new RepositoryModelBuilder();
        builder.ApplyConfigurationsFromAssembly(typeof(AssemblyConfigurationTests).Assembly, type => type == typeof(AConfiguration))
            .ApplyConfigurationsFromAssembly(typeof(AssemblyConfigurationTests).Assembly, type => type == typeof(AConfiguration));

        builder.Build().Entities.Should().ContainSingle().Which.Queries.Should().HaveCount(2);
    }

    private static RepositoryDesign CreateModel(params Type[] configurations) => new RepositoryModelBuilder()
        .ApplyConfigurationsFromAssembly(typeof(AssemblyConfigurationTests).Assembly, configurations.Contains).Build();

    private sealed class Row
    {
        public int Id { get; set; }
    }

    private sealed class OtherRow
    {
        public int Id { get; set; }
    }

    // Deliberately declared before A: reflection declaration order is not configuration order.
    private sealed class ZConfiguration : IRepositoryEntityConfiguration<Row>
    {
        private ZConfiguration() { }

        public void Configure(RepositoryEntityBuilder<Row> entity)
        {
            entity.WriteEnabled = true;
            entity.HasQuery("Z").Count();
        }
    }

    private sealed class AConfiguration : IRepositoryEntityConfiguration<Row>
    {
        public void Configure(RepositoryEntityBuilder<Row> entity)
        {
            entity.WriteEnabled = false;
            entity.HasQuery("A").Count();
        }
    }

    private abstract class GenericConfiguration<TEntity> : IRepositoryEntityConfiguration<TEntity> where TEntity : class
    {
        public void Configure(RepositoryEntityBuilder<TEntity> entity) => entity.HasQuery("Inherited").Count();
    }

    private abstract class AbstractConfiguration : GenericConfiguration<Row>;

    private sealed class InheritedConfiguration : AbstractConfiguration;

    private sealed class CombinedConfiguration : IRepositoryModelConfiguration,
        IRepositoryEntityConfiguration<Row>, IRepositoryEntityConfiguration<OtherRow>
    {
        private int stage;

        void IRepositoryModelConfiguration.Configure(RepositoryModelBuilder builder)
        {
            stage.Should().Be(0);
            stage++;
            builder.Entity<Row>().ReadEnabled = false;
        }

        void IRepositoryEntityConfiguration<OtherRow>.Configure(RepositoryEntityBuilder<OtherRow> entity)
        {
            stage.Should().Be(1);
            stage++;
            entity.HasQuery("Other").Count();
        }

        void IRepositoryEntityConfiguration<Row>.Configure(RepositoryEntityBuilder<Row> entity)
        {
            stage.Should().Be(2);
            entity.ReadEnabled = true;
        }
    }

    private sealed class StatefulConfiguration : IRepositoryEntityConfiguration<Row>
    {
        private readonly bool enabled;

        public StatefulConfiguration(bool enabled) => this.enabled = enabled;

        public void Configure(RepositoryEntityBuilder<Row> entity) => entity.WriteEnabled = enabled;
    }

    private sealed class ThrowingConstructor : IRepositoryEntityConfiguration<Row>
    {
        public ThrowingConstructor() => throw new InvalidOperationException("constructor failure");

        public void Configure(RepositoryEntityBuilder<Row> entity) => entity.WriteEnabled = false;
    }

    private sealed class ThrowingEntityConfiguration : IRepositoryEntityConfiguration<Row>
    {
        void IRepositoryEntityConfiguration<Row>.Configure(RepositoryEntityBuilder<Row> entity) => throw new InvalidOperationException("entity failure");
    }

    private sealed class ThrowingModelConfiguration : IRepositoryModelConfiguration
    {
        void IRepositoryModelConfiguration.Configure(RepositoryModelBuilder builder) => throw new InvalidOperationException("model failure");
    }

    private sealed class UnloadableAssembly : Assembly
    {
        public override string FullName => "IncompleteConfigurationAssembly";

        public override Type[] GetTypes() => throw new ReflectionTypeLoadException([null!], [new FileNotFoundException("missing dependency")]);
    }
}
