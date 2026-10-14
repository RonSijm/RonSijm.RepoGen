// This example's application contracts; compiled by the shared EF-free Contracts project.
namespace RonSijm.RepoGen.ConfigurationExamples.Contracts;

public readonly record struct DirectoryCode(int Value);

public sealed record DirectorySummary(Guid Id, string Name);
