namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RelationshipKeyThrowingComparer : IEqualityComparer<string>
{
    public RelationshipKeyThrowingComparer() => throw new InvalidOperationException("relationship factory executed");
    public bool Equals(string? x, string? y) => StringComparer.OrdinalIgnoreCase.Equals(x, y);
    public int GetHashCode(string obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
}
