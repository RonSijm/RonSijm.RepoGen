namespace RonSijm.RepoGen.Tool.Tests;

public sealed record TranslatedWriteRequest(int Id, string Pattern, string Title, int ExpectedVersion = 1);
