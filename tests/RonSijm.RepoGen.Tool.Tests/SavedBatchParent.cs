namespace RonSijm.RepoGen.Tool.Tests;

public sealed class SavedBatchParent
{
    public int TenantId { get; set; }
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
    public ICollection<SavedBatchChild> Children { get; set; } = new List<SavedBatchChild>();
}
