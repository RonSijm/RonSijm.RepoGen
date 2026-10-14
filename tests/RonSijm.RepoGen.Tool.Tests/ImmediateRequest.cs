namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ImmediateRequest(int OwnerId, string Code = "", int Delta = 1, int ExpectedVersion = 1, string Partition = "A");
