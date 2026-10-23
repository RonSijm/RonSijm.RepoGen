using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>An explicitly registered infrastructure query root. It must use the supplied context, parameterize inputs, and remain queryable.</summary>
/// <remarks>The provider owns visibility/loading policies and SQL translation. Generated service ports expose neither this interface nor IQueryable.</remarks>
public interface IRepositoryQueryRoot<in TContext, in TRequest, out TResult> where TContext : DbContext
{
    IQueryable<TResult> CreateQuery(TContext context, TRequest request);
}