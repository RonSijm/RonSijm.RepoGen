namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MemoryCollectionItem(string Key, string Text, int ExpectedVersion = 1, bool Supplied = true);
