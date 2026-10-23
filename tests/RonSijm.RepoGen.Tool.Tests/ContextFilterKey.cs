namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ContextFilterKey(string Tenant, int FolderId = 1, bool IncludeDeleted = false);
