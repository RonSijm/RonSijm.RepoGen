using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.SqlServer;

public enum TemporalQueryKind
{
    All,
    AsOf,
    FromTo,
    Between,
    ContainedIn
}

/// <summary>Opt-in, read-only temporal query root for a registered operation. All timestamps must be UTC.</summary>
/// <remarks>Configure the request/time selectors in infrastructure DI. Select DTOs and period values in the designer result pipeline.</remarks>
[RepositoryProvider("Microsoft.EntityFrameworkCore.SqlServer")]
public sealed class TemporalQueryRoot<TContext, TRequest, TEntity> : IRepositoryQueryRoot<TContext, TRequest, TEntity>
    where TContext : DbContext where TEntity : class
{
    private readonly TemporalQueryKind kind;
    private readonly Func<TRequest, DateTime>? start;
    private readonly Func<TRequest, DateTime>? end;

    public TemporalQueryRoot(TemporalQueryKind kind = TemporalQueryKind.All, Func<TRequest, DateTime>? start = null, Func<TRequest, DateTime>? end = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind != TemporalQueryKind.All && start is null)
        {
            throw new ArgumentNullException(nameof(start));
        }

        if (kind is TemporalQueryKind.FromTo or TemporalQueryKind.Between or TemporalQueryKind.ContainedIn && end is null)
        {
            throw new ArgumentNullException(nameof(end));
        }

        this.kind = kind;
        this.start = start;
        this.end = end;
    }

    public IQueryable<TEntity> CreateQuery(TContext context, TRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Database.ProviderName != "Microsoft.EntityFrameworkCore.SqlServer")
        {
            throw new NotSupportedException("TemporalQueryRoot requires SQL Server.");
        }

        if (context.Model.FindEntityType(typeof(TEntity))?.IsTemporal() != true)
        {
            throw new InvalidOperationException("The entity is not configured as a SQL Server temporal table.");
        }

        var set = context.Set<TEntity>();
        if (kind == TemporalQueryKind.All)
        {
            return set.TemporalAll().AsNoTracking();
        }

        var from = RequireUtc(start!(request));
        if (kind == TemporalQueryKind.AsOf)
        {
            return set.TemporalAsOf(from).AsNoTracking();
        }

        var to = RequireUtc(end!(request));
        if (from >= to)
        {
            throw new ArgumentException("The temporal range must have an end after its start.");
        }

        return (kind switch
        {
            TemporalQueryKind.FromTo => set.TemporalFromTo(from, to),
            TemporalQueryKind.Between => set.TemporalBetween(from, to),
            TemporalQueryKind.ContainedIn => set.TemporalContainedIn(from, to),
            _ => throw new InvalidOperationException("Unknown temporal query kind.")
        }).AsNoTracking();
    }

    private static DateTime RequireUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("Temporal query timestamps must use DateTimeKind.Utc.");
}