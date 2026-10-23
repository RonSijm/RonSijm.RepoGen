using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Tool.Tests;

internal sealed class ReturningShadowObserver : IRepositoryCommitObserver
{
    internal int Calls { get; private set; }
    internal IReadOnlyCollection<string> Tags { get; private set; } = [];
    public Task CommittedAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default)
    {
        Calls++; Tags = tags; return Task.CompletedTask;
    }
}
