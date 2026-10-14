namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScalarEquality;

public sealed record ComparisonNoteChange(int Id, string? Caption, string ExpectedRevision = "V1", string Revision = "V2");
