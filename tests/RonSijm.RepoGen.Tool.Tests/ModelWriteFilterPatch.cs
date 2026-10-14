namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ModelWriteFilterPatch(int Id, string Title, int ExpectedVersion = 0, bool Deleted = false, bool Hidden = false);
