namespace RonSijm.RepoGen.Tool.Tests;

public sealed record LinkWriteRequest(int TenantId, int OwnerId, IReadOnlyList<LinkWriteItem>? Items, int ExpectedVersion = 1);
