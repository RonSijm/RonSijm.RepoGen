namespace RonSijm.RepoGen.Testing.Tests;

internal sealed record InMemoryCollectionItem(string Key, string Text, int ExpectedVersion = 1, bool Supplied = true);
