using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

#pragma warning disable CA1822, CA1859 // Exercise instance discovery and the shared marker return type.

public sealed class OperationConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigureRunsFirstAndAllBuilderFamiliesUseTheSameInstanceAndEntity(bool scanAssembly)
    {
        var configuration = new MixedConfiguration();
        var builder = new RepositoryModelBuilder();
        if (scanAssembly)
        {
            builder.ApplyConfigurationsFromAssembly(typeof(OperationConfigurationTests).Assembly, type => type == typeof(MixedConfiguration));
        }
        else
        {
            builder.ApplyConfiguration(configuration);
            configuration.Calls.Should().Equal("Configure", "ZEntity", "MCommand", "AResult");
        }

        var entity = builder.Build().Entities.Should().ContainSingle().Subject;
        entity.ProjectionEnabled.Should().BeFalse();
        entity.Queries.Select(query => query.MethodName).Should().Equal("Inline", "ZEntity", "AResult");
        entity.Commands.Should().ContainSingle().Which.MethodName.Should().Be("MCommand");
    }

    [Fact]
    public void InheritedMethodsAndVirtualOverridesAreInvokedOnce()
    {
        var entity = new RepositoryModelBuilder().ApplyConfiguration(new DerivedConfiguration())
            .Build().Entities.Should().ContainSingle().Subject;

        entity.Queries.Select(query => query.MethodName).Should().Equal("Inherited", "Override");
    }

    [Fact]
    public void MultipleEntityContractsInvokeOnlyTheirMatchingOperationMethods()
    {
        var configuration = new CombinedConfiguration();
        var model = new RepositoryModelBuilder()
            .ApplyConfigurationsFromAssembly(typeof(OperationConfigurationTests).Assembly, type => type == typeof(CombinedConfiguration)).Build();

        model.Entities.Single(entity => entity.EntityType == typeof(Row)).Queries.Should().ContainSingle().Which.MethodName.Should().Be("Row");
        model.Entities.Single(entity => entity.EntityType == typeof(OtherRow)).Queries.Should().ContainSingle().Which.MethodName.Should().Be("Other");
        // Also cover explicit registration of one contract on a module implementing both.
        new RepositoryModelBuilder().ApplyConfiguration<Row>(configuration).Build().Entities.Should().ContainSingle();
    }

    [Fact]
    public void PrivateHelpersUnrelatedMethodsAndPropertyGettersAreNotDiscovered()
    {
        var entity = new RepositoryModelBuilder().ApplyConfiguration(new HelpersConfiguration())
            .Build().Entities.Should().ContainSingle().Subject;

        entity.Queries.Should().ContainSingle().Which.MethodName.Should().Be("Helper");
    }

    [Theory]
    [InlineData(typeof(NoParameterConfiguration))]
    [InlineData(typeof(ExtraParameterConfiguration))]
    [InlineData(typeof(OtherEntityConfiguration))]
    [InlineData(typeof(StaticMethodConfiguration))]
    [InlineData(typeof(GenericMethodConfiguration))]
    public void InvalidOperationSignaturesIdentifyTheConfigurationAndMethod(Type configurationType)
    {
        var configuration = (IRepositoryEntityConfiguration<Row>)Activator.CreateInstance(configurationType, nonPublic: true)!;
        var apply = () => new RepositoryModelBuilder().ApplyConfiguration(configuration);

        apply.Should().Throw<InvalidOperationException>().WithMessage("RGD1007:*" + configurationType.FullName + ".Invalid*RepositoryEntityBuilder<TEntity>*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvocationFailuresIdentifyTheMethodAndPreserveTheOriginalException(bool scanAssembly)
    {
        var builder = new RepositoryModelBuilder();
        Action apply = scanAssembly
            ? () => builder.ApplyConfigurationsFromAssembly(typeof(OperationConfigurationTests).Assembly, type => type == typeof(ThrowingConfiguration))
            : () => builder.ApplyConfiguration(new ThrowingConfiguration());

        var exception = apply.Should().Throw<InvalidOperationException>().WithMessage("*RGD1007:*ThrowingConfiguration.Fail*").Which;
        if (scanAssembly)
        {
            exception = exception.InnerException.Should().BeOfType<InvalidOperationException>().Subject;
        }
        exception.InnerException.Should().BeOfType<ArgumentException>().Which.Message.Should().Be("operation failure");
    }

    [Fact]
    public void NullBuilderResultsAreRejected()
    {
        var apply = () => new RepositoryModelBuilder().ApplyConfiguration(new NullConfiguration());

        apply.Should().Throw<InvalidOperationException>().WithMessage("RGD1007:*NullConfiguration.Missing*returned null*");
    }

    [Fact]
    public void RepeatedApplicationsRunOperationMethodsAgain()
    {
        var configuration = new MarkerConfiguration();
        var entity = new RepositoryModelBuilder().ApplyConfiguration(configuration).ApplyConfiguration(configuration)
            .Build().Entities.Should().ContainSingle().Subject;

        entity.Queries.Select(query => query.MethodName).Should().Equal("Count", "Count");
    }

    private sealed class Row
    {
        public int Id { get; set; }
    }

    private sealed class OtherRow
    {
        public int Id { get; set; }
    }

    private sealed class MixedConfiguration : IRepositoryEntityConfiguration<Row>
    {
        private RepositoryEntityBuilder<Row> configuredEntity = null!;
        private RepositoryQueryFragment<Row> visible = null!;
        public List<string> Calls { get; } = [];

        public void Configure(RepositoryEntityBuilder<Row> entity)
        {
            Calls.Add("Configure");
            configuredEntity = entity;
            entity.ProjectionEnabled = false;
            visible = entity.HasFragment("Visible", row => row.Id > 0);
            entity.HasQuery("Inline").Count();
        }

        // Deliberately declared in reverse name order: operation methods preserve declaration order.
        public RepositoryQueryBuilder<Row> ZEntity(RepositoryEntityBuilder<Row> entity)
        {
            entity.Should().BeSameAs(configuredEntity);
            Calls.Add(nameof(ZEntity));
            return entity.HasQuery(nameof(ZEntity)).Use(visible).Count();
        }

        public RepositoryCommandBuilder<Row> MCommand(RepositoryEntityBuilder<Row> entity)
        {
            entity.Should().BeSameAs(configuredEntity);
            Calls.Add(nameof(MCommand));
            return entity.HasCommand(nameof(MCommand)).Where<int>((row, id) => row.Id == id).TrackedDelete().Save();
        }

        public RepositoryResultQueryBuilder<int> AResult(RepositoryEntityBuilder<Row> entity)
        {
            entity.Should().BeSameAs(configuredEntity);
            Calls.Add(nameof(AResult));
            return entity.HasQuery(nameof(AResult)).Use(visible).Select(row => row.Id).List();
        }
    }

    private abstract class BaseConfiguration : IRepositoryEntityConfiguration<Row>
    {
        public void Configure(RepositoryEntityBuilder<Row> entity) => entity.ProjectionEnabled = false;
        public RepositoryQueryBuilder<Row> Inherited(RepositoryEntityBuilder<Row> entity) => entity.HasQuery("Inherited").Count();
        public virtual RepositoryQueryBuilder<Row> Overridden(RepositoryEntityBuilder<Row> entity) => throw new InvalidOperationException("base must not run");
    }

    private sealed class DerivedConfiguration : BaseConfiguration
    {
        public override RepositoryQueryBuilder<Row> Overridden(RepositoryEntityBuilder<Row> entity) => entity.HasQuery("Override").Count();
    }

    private sealed class CombinedConfiguration : IRepositoryEntityConfiguration<Row>, IRepositoryEntityConfiguration<OtherRow>
    {
        void IRepositoryEntityConfiguration<Row>.Configure(RepositoryEntityBuilder<Row> entity) => entity.WriteEnabled = false;
        void IRepositoryEntityConfiguration<OtherRow>.Configure(RepositoryEntityBuilder<OtherRow> entity) => entity.WriteEnabled = false;
        public RepositoryQueryBuilder<Row> Count(RepositoryEntityBuilder<Row> entity) => entity.HasQuery("Row").Count();
        public RepositoryQueryBuilder<OtherRow> Count(RepositoryEntityBuilder<OtherRow> entity) => entity.HasQuery("Other").Count();
    }

    private sealed class HelpersConfiguration : IRepositoryEntityConfiguration<Row>
    {
        public IRepositoryOperationBuilder Property => throw new InvalidOperationException("property getter must not run");
        public void Configure(RepositoryEntityBuilder<Row> entity) => Helper(entity);
        private static RepositoryQueryBuilder<Row> Helper(RepositoryEntityBuilder<Row> entity) => entity.HasQuery("Helper").Count();
        public string Unrelated() => throw new InvalidOperationException("unrelated method must not run");
        public void VoidHelper() => throw new InvalidOperationException("void helper must not run");
    }

    private abstract class EmptyConfiguration : IRepositoryEntityConfiguration<Row>
    {
        public void Configure(RepositoryEntityBuilder<Row> entity) => entity.WriteEnabled = false;
    }

    private sealed class NoParameterConfiguration : EmptyConfiguration
    {
        public IRepositoryOperationBuilder Invalid() => throw new NotSupportedException();
    }

    private sealed class ExtraParameterConfiguration : EmptyConfiguration
    {
        public IRepositoryOperationBuilder Invalid(RepositoryEntityBuilder<Row> entity, int extra) => throw new NotSupportedException();
    }

    private sealed class OtherEntityConfiguration : EmptyConfiguration
    {
        public IRepositoryOperationBuilder Invalid(RepositoryEntityBuilder<OtherRow> entity) => throw new NotSupportedException();
    }

    private sealed class StaticMethodConfiguration : EmptyConfiguration
    {
        public static IRepositoryOperationBuilder Invalid(RepositoryEntityBuilder<Row> entity) => throw new NotSupportedException();
    }

    private sealed class GenericMethodConfiguration : EmptyConfiguration
    {
        public IRepositoryOperationBuilder Invalid<T>(RepositoryEntityBuilder<Row> entity) => throw new NotSupportedException();
    }

    private sealed class ThrowingConfiguration : EmptyConfiguration
    {
        public RepositoryQueryBuilder<Row> Fail(RepositoryEntityBuilder<Row> entity) => throw new ArgumentException("operation failure");
    }

    private sealed class NullConfiguration : EmptyConfiguration
    {
        public RepositoryQueryBuilder<Row> Missing(RepositoryEntityBuilder<Row> entity) => null!;
    }

    private sealed class MarkerConfiguration : EmptyConfiguration
    {
        public IRepositoryOperationBuilder Count(RepositoryEntityBuilder<Row> entity) => entity.HasQuery(nameof(Count)).Count();
    }
}
