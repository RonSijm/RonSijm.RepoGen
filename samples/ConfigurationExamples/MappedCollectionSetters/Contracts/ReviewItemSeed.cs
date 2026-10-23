namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedCollectionSetters;

public sealed record ReviewItemSeed(int Id, int BoardId, string Code, string Caption, int Edits, int Revision, string? Remark);
