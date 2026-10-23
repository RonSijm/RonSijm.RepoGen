using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicCapture;

public interface ICapturedReservationWriter
{
    Task<CommandResult<ReservationReceipt>> EnsureAsync(ReservationChange request, CancellationToken cancellationToken = default);
    Task<CommandResult<ReservationReceipt>> PutAsync(ReservationChange request, CancellationToken cancellationToken = default);
}
