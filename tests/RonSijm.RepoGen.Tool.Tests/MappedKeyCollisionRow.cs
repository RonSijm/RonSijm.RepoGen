using System.Diagnostics.CodeAnalysis;

namespace RonSijm.RepoGen.Tool.Tests;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Exercises generated-member collisions with an unassigned mapped CLR property.")]
internal sealed class MappedKeyCollisionRow
{
    public int Id { get; set; }
    public int __repoGenProperty0 { get; set; }
    public List<MappedKeyCollisionChild> Children { get; } = [];
}
