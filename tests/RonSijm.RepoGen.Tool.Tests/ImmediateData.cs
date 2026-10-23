namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ImmediateData(int Key, int OwnerId, string Code, int Amount, int Previous, bool Hidden, int Revision, byte[] Marker, string Tag = "arranged");
