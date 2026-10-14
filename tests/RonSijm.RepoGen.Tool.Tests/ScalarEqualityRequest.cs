namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ScalarEqualityRequest(int Id, string? Label, string ExpectedRevision = "V1", string Revision = "V2", int Amount = 1, string Partition = "A");
