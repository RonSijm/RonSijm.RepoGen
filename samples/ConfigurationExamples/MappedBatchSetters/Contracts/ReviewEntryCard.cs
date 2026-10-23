namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedBatchSetters;

public sealed record ReviewEntryCard(int Id, string Label, string Caption, int Edits, int Revision, string? Remark);
