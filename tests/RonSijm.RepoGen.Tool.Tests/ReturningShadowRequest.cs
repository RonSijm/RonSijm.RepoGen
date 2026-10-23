namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ReturningShadowRequest(string Prefix, int Delta, string Suffix, int ExpectedVersion);
