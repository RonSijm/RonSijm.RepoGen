namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProjectionNoteThrowingDto
{
    public ProjectionNoteThrowingDto(int id) => throw new InvalidOperationException("DTO constructor must not run during generation: " + id);
    public int Id { get; }
}
