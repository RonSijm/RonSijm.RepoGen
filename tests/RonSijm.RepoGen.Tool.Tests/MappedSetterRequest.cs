namespace RonSijm.RepoGen.Tool.Tests;

public sealed record MappedSetterRequest(int Id = 1, string Caption = "new", string? Extra = "extra", int Version = 3, bool ChangeCaption = true);
