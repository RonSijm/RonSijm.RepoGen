namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MultipleSharedLinkRequest(int Zone, int SpaceId, int OwnerId, IReadOnlyList<MultipleSharedLinkItem> Items);
