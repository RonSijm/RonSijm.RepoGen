using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed record LinkWriteOptionalRequest(int TenantId, int OwnerId, Optional<IReadOnlyList<LinkWriteItem>?> Items);
