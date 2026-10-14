namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedResultRejectedView
{
    public SavedResultRejectedView(string name) => throw new InvalidOperationException("Saved DTO rejected: " + name);
}
