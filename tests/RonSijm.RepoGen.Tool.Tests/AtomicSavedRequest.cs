namespace RonSijm.RepoGen.Tool.Tests;

public sealed record AtomicSavedRequest(int ParentId, string Key, string Text, int ExpectedVersion = 1, bool Hidden = false);
