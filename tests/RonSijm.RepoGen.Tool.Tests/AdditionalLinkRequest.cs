namespace RonSijm.RepoGen.Tool.Tests;

public sealed record AdditionalLinkRequest(int Scope, int OwnerId, IReadOnlyList<AdditionalLinkItem>? Items, int ExpectedVersion = 1);
