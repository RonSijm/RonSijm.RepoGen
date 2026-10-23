using System.Linq.Expressions;
using FluentAssertions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.InMemoryTranslationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryTranslationGuardTests
{
    [Theory]
    [InlineData("computed", "RGD4001")]
    [InlineData("notCall", "RGD4001")]
    [InlineData("repeated", "RGD4001")]
    [InlineData("byRef", "RGD4001")]
    [InlineData("captured", "RGD4002")]
    [InlineData("clock", "RGD4002")]
    [InlineData("helper", "RGD4002")]
    [InlineData("graph", "RGD4002")]
    [InlineData("dropped", "RGD4002")]
    [InlineData("cycle", "RGD4003")]
    public void UnsafePatternsReplacementsAndCyclesFailWithoutExecutingEitherLambda(string invalid, string diagnostic)
    {
        using var context = InMemoryQueryTestHarness.Context();
        var builder = InMemoryQueryTestHarness.Builder(); InMemoryQueryTestHarness.Entity(builder).HasQuery("Count").Count();
        var threshold = 5;
        switch (invalid)
        {
            case "computed": builder.HasInMemoryTranslation<int, bool>("Unsafe", value => TranslationCalls.Positive(value + 1), value => value > 0); break;
            case "notCall": builder.HasInMemoryTranslation<int, bool>("Unsafe", value => value > 0, value => value > 0); break;
            case "repeated": builder.HasInMemoryTranslation<int, int, int>("Unsafe", (value, amount) => TranslationCalls.Shift(value, value), (value, amount) => value + amount); break;
            case "byRef": builder.HasInMemoryTranslation<int, int>("Unsafe", value => Interlocked.Increment(ref value), value => value + 1); break;
            case "captured": builder.HasInMemoryTranslation<int, bool>("Unsafe", value => TranslationCalls.Positive(value), value => value > threshold); break;
            case "clock": builder.HasInMemoryTranslation<int, bool>("Unsafe", value => TranslationCalls.Positive(value), value => value > DateTime.UtcNow.Second); break;
            case "helper": builder.HasInMemoryTranslation<int, bool>("Unsafe", value => TranslationCalls.Positive(value), value => TranslationCalls.Echo(value) > 0); break;
            case "graph": builder.HasInMemoryTranslation<int, bool>("Unsafe", value => TranslationCalls.Positive(value), value => new PortableRow { Id = value }.Id > 0); break;
            case "dropped": builder.HasInMemoryTranslation<int, bool>("Unsafe", value => TranslationCalls.Positive(value), _ => true); break;
            case "cycle": builder.HasInMemoryTranslation<int, bool>("Unsafe", value => TranslationCalls.Positive(value), value => TranslationCalls.Positive(value)); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(diagnostic + ":*");
    }

    [Fact]
    public void NamesAndExactOverloadsAreUniqueAcrossModules()
    {
        var builder = Builder();
        Action duplicateMethod = () => builder.HasInMemoryTranslation<string, int>("OtherLength", value => TranslationCalls.Measure(value), value => value.Length);
        Action duplicateName = () => builder.HasInMemoryTranslation<int, int, int, int>("Length", (a, b, c) => TranslationCalls.Three(a, b, c), (a, b, c) => a + b + c);
        duplicateMethod.Should().Throw<InvalidOperationException>().WithMessage("RGD4001:*"); duplicateName.Should().Throw<InvalidOperationException>().WithMessage("RGD4001:*");
        Action missingName = () => builder.HasInMemoryTranslation<int, bool>(" ", value => TranslationCalls.Positive(value), value => value > 0);
        missingName.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NestedCallsAndAcyclicMappingsSupportInstanceReceiversAndAllTypedArities()
    {
        var builder = InMemoryQueryTestHarness.Builder();
        builder.HasInMemoryTranslation<string, string, bool>("Prefix", (value, prefix) => value.StartsWith(prefix), (value, prefix) => value.StartsWith(prefix, StringComparison.Ordinal));
        builder.HasInMemoryTranslation<int, int, int, int>("Three", (a, b, c) => TranslationCalls.Three(a, b, c), (a, b, c) => a + b + c);
        builder.HasInMemoryTranslation<int, int, int, int, int>("Four", (a, b, c, d) => TranslationCalls.Four(a, b, c, d), (a, b, c, d) => TranslationCalls.Three(a, b, c) + d);
        var visitor = new InMemoryTranslationExpressions(builder.Build().InMemoryTranslations);
        Expression<Func<int, int>> expression = value => TranslationCalls.Four(TranslationCalls.Four(value, 1, 2, 3), 4, 5, 6);
        var expanded = (Expression<Func<int, int>>)visitor.Expand(expression);
        // Test execution occurs here, never during generation or normalization.
        expanded.Compile()(10).Should().Be(31);
        visitor.Receipts.Select(receipt => receipt.Name).Should().Equal("Four", "Three");
    }
}
