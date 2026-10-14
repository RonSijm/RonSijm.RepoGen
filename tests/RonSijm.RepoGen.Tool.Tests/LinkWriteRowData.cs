namespace RonSijm.RepoGen.Tool.Tests;

public sealed record LinkWriteRowData(int OwnerTenantId, int OwnerId, string TargetRegion, string TargetCode, string Note, int Version, bool Hidden);
