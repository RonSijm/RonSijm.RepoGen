namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedBatchChild
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ParentId { get; set; }
    public string Text { get; set; } = "";
    public bool Hidden { get; set; }
    public SavedBatchParent? Parent { get; set; }
}
