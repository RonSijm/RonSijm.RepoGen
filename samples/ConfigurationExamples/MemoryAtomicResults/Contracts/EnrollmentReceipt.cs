namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicResults;

public sealed record EnrollmentReceipt(int Id, string Name, int Version, string Course, IReadOnlyList<string> Members);
