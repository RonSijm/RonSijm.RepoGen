namespace RonSijm.RepoGen.Tool.Tests;

public sealed record CompositeCollectionChildData(int Id, int TenantId, int ParentId, string Code, string Language, string Text, int Version = 1, bool Hidden = false);
