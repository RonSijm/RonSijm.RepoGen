namespace RonSijm.RepoGen.Tool.Tests;

internal sealed class ProxyReadEntity
{
    internal static int Constructions;
    public ProxyReadEntity() => Constructions++;
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Score { get; set; }
    public bool Visible { get; set; }
    public int DepartmentId { get; set; }
    public int Unmapped { get; set; }
    public byte[] Stamp { get; set; } = [];
    public ProxyReadDepartment? Department { get; set; }
    public string Computed { get; set; } = "";
    public HiddenMemoryKey PrivateCode { get; set; }
    public string? Optional { get; set; }
    public int? OptionalScore { get; set; }
    public new int GetHashCode { get; set; }
    public int __RepoGenProxyReadEntityReadRow { get; set; }
}
