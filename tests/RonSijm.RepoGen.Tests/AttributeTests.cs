using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Tests;

public sealed class AttributeTests
{
    [Fact]
    public void ProjectionAttributeStoresMethodNameOverride()
    {
        var attribute = new ProjectionFromAttribute<TestEntity>
        {
            MethodName = "AsCard"
        };

        attribute.MethodName.Should().Be("AsCard");
    }

    [Fact]
    public void AttributesHaveTheExpectedUsage()
    {
        var entityUsage = typeof(GenerateSelectorsAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();
        var projectionUsage = typeof(ProjectionFromAttribute<TestEntity>)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        entityUsage.ValidOn.Should().Be(AttributeTargets.Class);
        projectionUsage.ValidOn.Should().Be(AttributeTargets.Class);
        entityUsage.AllowMultiple.Should().BeFalse();
        projectionUsage.AllowMultiple.Should().BeFalse();
    }

    private sealed class TestEntity;
}