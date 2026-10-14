namespace RonSijm.RepoGen.Tool.Tests;

public sealed record SavedGraphFilterRequest(int Id, string Title, int? FolderId = null, bool Deleted = false, bool Hidden = false);
