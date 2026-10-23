using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Stages infrastructure-owned tracked changes before a shared unit-of-work save.</summary>
/// <remarks>
/// Runs inside an explicit unit-of-work transaction, on its actual context, before changed tags
/// are collected. Do not save, complete transactions, re-enter the work, or publish external effects.
/// Retries require fresh work/context state and replayable participant logic. This hook is not an outbox.
/// </remarks>
public interface IRepositorySaveParticipant<in TContext> where TContext : DbContext
{
    ValueTask SavingAsync(TContext context, CancellationToken cancellationToken = default);
}

/// <summary>Optional infrastructure-only registration; no participant is installed by default.</summary>
public interface IRepositorySaveParticipantSource<TContext> where TContext : DbContext
{
    /// <summary>Registers once per reference identity, before the first save. The first registration wins.</summary>
    void RegisterSaveParticipant(object identity, IRepositorySaveParticipant<TContext> participant);
}