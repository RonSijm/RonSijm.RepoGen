namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RelationshipKeyEntityComparer(ManyReadOwner owner) : IEqualityComparer<string>
{
    public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase) && owner.Id >= 0;
    public int GetHashCode(string obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj);
}
