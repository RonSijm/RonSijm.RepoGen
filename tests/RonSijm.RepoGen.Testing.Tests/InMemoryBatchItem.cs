namespace RonSijm.RepoGen.Testing.Tests;

internal sealed record InMemoryBatchItem(int Id, string Name, int ExpectedVersion = 1, bool Supplied = true);
