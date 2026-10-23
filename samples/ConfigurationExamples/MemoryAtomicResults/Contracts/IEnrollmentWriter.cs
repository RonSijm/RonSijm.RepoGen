using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicResults;

public interface IEnrollmentWriter
{
    Task<CommandResult<EnrollmentReceipt>> EnsureAsync(EnrollmentChange request, CancellationToken cancellationToken = default);
    Task<CommandResult<EnrollmentReceipt>> PutAsync(EnrollmentChange request, CancellationToken cancellationToken = default);
}
