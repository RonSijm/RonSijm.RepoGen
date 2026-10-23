namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ProxyWriteData(int Key, string Caption, int Revision, string Label, bool Archived, byte[] Binary, string Extra = "arranged");
