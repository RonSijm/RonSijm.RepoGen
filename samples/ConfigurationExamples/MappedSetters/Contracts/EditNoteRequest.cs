namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedSetters;

public sealed record EditNoteRequest(int Id, string Caption, string? Remark, int Version, bool ChangeCaption = true);
