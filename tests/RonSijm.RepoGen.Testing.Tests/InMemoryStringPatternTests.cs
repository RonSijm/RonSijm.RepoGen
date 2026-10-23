using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Testing.Tests;

public sealed class InMemoryStringPatternTests
{
    [Theory]
    [InlineData(null, "%", false)]
    [InlineData("a", null, false)]
    [InlineData("", "", true)]
    [InlineData("", "%", true)]
    [InlineData("", "_", false)]
    [InlineData("a", "", false)]
    [InlineData("alphabet", "%pha%", true)]
    [InlineData("alphabet", "alpha%", true)]
    [InlineData("alphabet", "%bet", true)]
    [InlineData("alphabet", "a__ha%", true)]
    [InlineData("alphabet", "a_ha%", false)]
    [InlineData("a", "%%a%%", true)]
    [InlineData("a_b", "a_b", true)]
    [InlineData("a_b", "a\\_b", false)]
    [InlineData("[abc]", "[abc]", true)]
    [InlineData("a", "[abc]", false)]
    [InlineData("a\nb", "a%b", true)]
    [InlineData("A", "a", false)]
    [InlineData("😀", "_", false)]
    [InlineData("😀", "__", true)]
    public void PatternsHaveExplicitOrdinalUtf16Semantics(string? value, string? pattern, bool matches)
        => InMemoryStringPatterns.Like(value, pattern).Should().Be(matches);

    [Theory]
    [InlineData("A_B", "a\\_b", true)]
    [InlineData("a%b", "a\\%b", true)]
    [InlineData("ab", "a\\%b", false)]
    [InlineData("a\\b", "a\\\\b", true)]
    [InlineData("AXb", "a_b", true)]
    [InlineData("a%b", "a!%b", true)]
    public void EscapingAndOrdinalIgnoreCaseAreExplicit(string value, string pattern, bool matches)
        => InMemoryStringPatterns.Like(value, pattern, StringComparison.OrdinalIgnoreCase, pattern.Contains('!') ? '!' : '\\').Should().Be(matches);

    [Theory]
    [InlineData(StringComparison.CurrentCulture)]
    [InlineData(StringComparison.CurrentCultureIgnoreCase)]
    [InlineData(StringComparison.InvariantCulture)]
    [InlineData(StringComparison.InvariantCultureIgnoreCase)]
    [InlineData((StringComparison)999)]
    public void AmbientCultureCannotBeMistakenForADatabaseCollation(StringComparison comparison)
    {
        Action match = () => InMemoryStringPatterns.Like("a", "a", comparison);
        match.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DanglingEscapesFailWithoutARegexOrCatastrophicBacktracking()
    {
        Action invalid = () => InMemoryStringPatterns.Like("a", "a\\", StringComparison.Ordinal, '\\');
        invalid.Should().Throw<ArgumentException>().WithParameterName("pattern");
        InMemoryStringPatterns.Like(new string('a', 200), string.Concat(Enumerable.Repeat("%a", 200)) + "b").Should().BeFalse();
    }
}
