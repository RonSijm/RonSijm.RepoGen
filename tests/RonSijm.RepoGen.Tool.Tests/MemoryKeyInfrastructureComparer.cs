using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MemoryKeyInfrastructureComparer(List<DbContext> contexts) : IEqualityComparer<string>
{
    public bool Equals(string? x, string? y) => StringComparer.Ordinal.Equals(x, y) && contexts.Count == 0;
    public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(obj);
}
