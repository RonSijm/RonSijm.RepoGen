namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicResults;

public sealed record EnrollmentChange(int Id, int CourseId, string Code, string Name, int ExpectedVersion = 1);
