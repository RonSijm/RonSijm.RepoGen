namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ProxyWriteRequest(int Id, string Name, int ExpectedVersion = 3, string Label = null!, bool Supplied = true, byte[]? Stamp = null);
