namespace RonSijm.RepoGen.Tool.Tests;

public sealed record CreateManyRequest(IReadOnlyList<CreateManyItem> Items, string Label = "request", string Partition = "a");
