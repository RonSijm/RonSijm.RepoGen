namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedCollectionSetters;

public sealed record ReviewItemEdit(string Code, string Caption, int Revision, string? Remark = null, bool ChangeCaption = true);
