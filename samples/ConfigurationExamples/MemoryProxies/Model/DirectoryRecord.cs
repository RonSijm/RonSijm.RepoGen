namespace RonSijm.RepoGen.ConfigurationExamples.Models.ProxyDirectory;

// Deliberately internal and absent from the public contracts/testing consumer.
internal sealed class DirectoryRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Points { get; set; }
    public bool Disabled { get; set; }
    public string InternalTag { get; set; } = "";
}
