using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Applies an operation-local timeout and restores a borrowed context's previous setting, including on early stream disposal.</summary>
public sealed class RepositoryCommandTimeoutScope : IDisposable
{
    private readonly DbContext context;
    private readonly int? original;
    private bool disposed;

    public RepositoryCommandTimeoutScope(DbContext context, int seconds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, 1);
        this.context = context;
        original = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(seconds);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            context.Database.SetCommandTimeout(original);
        }
    }
}