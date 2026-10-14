namespace RonSijm.RepoGen.Testing.Tests;

internal sealed class InMemoryBinaryWriteRow
{
    public int Id { get; set; }
    public byte[] Data { get; set; } = [];
    public byte[] Token { get; set; } = [];
    public static InMemoryBinaryWriteRow Copy(InMemoryBinaryWriteRow row) => new() { Id = row.Id, Data = row.Data.ToArray(), Token = row.Token.ToArray() };
}
