namespace RonSijm.RepoGen.Tool.Tests;

public sealed record SavedBatchItem(string Code, string Name, int ExpectedVersion = 1, bool Supplied = true);
