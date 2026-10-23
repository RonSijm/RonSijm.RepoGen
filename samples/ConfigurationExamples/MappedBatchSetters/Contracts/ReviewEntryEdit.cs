namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedBatchSetters;

public sealed record ReviewEntryEdit(int Id, string Caption, int Revision, string? Remark = null, bool ChangeCaption = true);
