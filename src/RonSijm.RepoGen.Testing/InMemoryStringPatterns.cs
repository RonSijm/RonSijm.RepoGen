namespace RonSijm.RepoGen.Testing;

/// <summary>Explicit ordinal fixture semantics for percent/underscore patterns, not a database collation emulator.</summary>
public static class InMemoryStringPatterns
{
    public static bool Like(string? value, string? pattern) => Like(value, pattern, StringComparison.Ordinal, null);

    public static bool Like(string? value, string? pattern, StringComparison comparison) => Like(value, pattern, comparison, null);

    /// <remarks>Percent matches zero or more UTF-16 code units; underscore matches one. Escapes quote the next unit.
    /// Brackets are literals. Only Ordinal/OrdinalIgnoreCase are supported. Null inputs do not match.</remarks>
    public static bool Like(string? value, string? pattern, StringComparison comparison, char? escapeCharacter)
    {
        if (comparison is not StringComparison.Ordinal and not StringComparison.OrdinalIgnoreCase)
            throw new ArgumentOutOfRangeException(nameof(comparison), "Fixture patterns require an explicit ordinal comparison.");
        if (value is null || pattern is null) return false;
        var previous = new bool[value.Length + 1];
        var current = new bool[value.Length + 1];
        previous[0] = true;
        for (var position = 0; position < pattern.Length; position++)
        {
            var literal = pattern[position] == escapeCharacter;
            if (literal && ++position == pattern.Length)
                throw new ArgumentException("A pattern cannot end with an unmatched escape character.", nameof(pattern));
            var symbol = pattern[position];
            var many = !literal && symbol == '%';
            var one = !literal && symbol == '_';
            current[0] = many && previous[0];
            for (var index = 1; index <= value.Length; index++)
                current[index] = many ? previous[index] || current[index - 1] : previous[index - 1] &&
                    (one || value.AsSpan(index - 1, 1).Equals(pattern.AsSpan(position, 1), comparison));
            (previous, current) = (current, previous);
        }
        return previous[value.Length];
    }
}
