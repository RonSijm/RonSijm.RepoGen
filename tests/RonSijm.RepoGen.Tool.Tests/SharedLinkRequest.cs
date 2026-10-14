namespace RonSijm.RepoGen.Tool.Tests;

public sealed record SharedLinkRequest(int SpaceId, int OwnerId, IReadOnlyList<SharedLinkItem>? Items,
    int ExpectedVersion = 1, bool Apply = true);
