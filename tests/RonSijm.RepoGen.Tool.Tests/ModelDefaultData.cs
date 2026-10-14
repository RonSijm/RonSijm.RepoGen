namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ModelDefaultData(int Id, int Credits = -1, bool Enabled = true, ModelDefaultLevel Level = ModelDefaultLevel.Unset,
    string? Label = null, int? Score = -1, byte Priority = byte.MaxValue);
