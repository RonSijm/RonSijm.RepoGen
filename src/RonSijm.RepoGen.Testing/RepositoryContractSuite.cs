using System.Runtime.ExceptionServices;

namespace RonSijm.RepoGen.Testing;

/// <summary>Runs the same application-port assertions with a fresh explicitly supplied session per case.</summary>
/// <remarks>Framework/provider independent, sequential and fail-fast. The factory owns seed/reset isolation;
/// a new session does not itself reset a database. This is observable contract testing, not proof of SQL equivalence.</remarks>
public sealed class RepositoryContractSuite<TRepository> where TRepository : class
{
    private readonly RepositoryContractCase<TRepository>[] cases;

    public RepositoryContractSuite(IEnumerable<RepositoryContractCase<TRepository>> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        this.cases = cases.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contractCase in this.cases)
        {
            ArgumentNullException.ThrowIfNull(contractCase);
            if (!names.Add(contractCase.Name))
            {
                throw new ArgumentException("Duplicate repository contract case name: " + contractCase.Name, nameof(cases));
            }
        }
        CaseNames = Array.AsReadOnly(this.cases.Select(static item => item.Name).ToArray());
    }

    public IReadOnlyList<string> CaseNames { get; }

    public async Task RunAsync(Func<CancellationToken, ValueTask<RepositoryContractSession<TRepository>>> openSession,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openSession);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var contractCase in cases)
        {
            await RunCaseAsync(contractCase, openSession, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task RunCaseAsync(RepositoryContractCase<TRepository> contractCase,
        Func<CancellationToken, ValueTask<RepositoryContractSession<TRepository>>> openSession, CancellationToken token)
    {
        RepositoryContractSession<TRepository>? session = null;
        Exception? failure = null;
        var phase = RepositoryContractFailurePhase.OpenSession;
        try
        {
            token.ThrowIfCancellationRequested();
            session = await openSession(token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The contract session factory returned null.");
            token.ThrowIfCancellationRequested();
            phase = RepositoryContractFailurePhase.Assertion;
            var assertion = contractCase.Assertion(session.Repository, token)
                ?? throw new InvalidOperationException("The contract assertion returned a null Task.");
            await assertion.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (session is not null)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (failure is null)
                {
                    phase = RepositoryContractFailurePhase.Cleanup;
                    failure = exception;
                }
                else
                {
                    failure = new AggregateException(failure, exception);
                }
            }
        }

        if (failure is OperationCanceledException && token.IsCancellationRequested)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        if (failure is not null)
        {
            throw new RepositoryContractFailureException(contractCase.Name, phase, failure);
        }
        token.ThrowIfCancellationRequested();
    }
}
