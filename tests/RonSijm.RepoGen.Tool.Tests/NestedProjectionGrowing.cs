namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NestedProjectionGrowing<T>
{
    public int Id { get; set; }
    public NestedProjectionGrowing<NestedProjectionGrowing<T>>? Next { get; set; }
}
