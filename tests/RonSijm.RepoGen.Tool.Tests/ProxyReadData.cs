namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProxyReadData
{
    public int Key { get; set; }
    public string Caption { get; set; } = "";
    public string Group { get; set; } = "";
    public int RawScore { get; set; }
    public bool Archived { get; set; }
    public int DepartmentKey { get; set; }
    public byte[] Stamp { get; set; } = [];
}
