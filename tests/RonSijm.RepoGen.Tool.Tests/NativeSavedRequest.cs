namespace RonSijm.RepoGen.Tool.Tests;

public sealed record NativeSavedRequest(int ParentId, string Text = "changed:", int ExpectedVersion = 1, bool Hidden = false, string Partition = "first");
