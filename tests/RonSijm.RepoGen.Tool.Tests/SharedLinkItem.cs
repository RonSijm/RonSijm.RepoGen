namespace RonSijm.RepoGen.Tool.Tests;

public sealed record SharedLinkItem(int Scope, int TargetId, string Note, int ExpectedVersion = 1);
