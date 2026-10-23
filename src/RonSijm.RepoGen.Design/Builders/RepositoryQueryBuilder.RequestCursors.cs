using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryQueryBuilder<TEntity> where TEntity : class
{
    /// <summary>Seeks using a typed request and maps a cursor from the selected entity.</summary>
    public RepositoryQueryBuilder<TEntity> CursorPaged<TCursor, TRequest>(
        Expression<Func<TEntity, TCursor, TRequest, bool>> seekPredicate,
        Expression<Func<TEntity, TCursor>> cursorSelector) =>
        RequestCursor(RepositoryCursorDesign.RequestBound(typeof(TCursor), seekPredicate, cursorSelector));

    /// <summary>The seek request's parameter name is also used by the cursor mapper.</summary>
    public RepositoryQueryBuilder<TEntity> CursorPaged<TCursor, TRequest>(
        Expression<Func<TEntity, TCursor, TRequest, bool>> seekPredicate,
        Expression<Func<TEntity, TRequest, TCursor>> cursorSelector) =>
        RequestCursor(RepositoryCursorDesign.RequestBound(typeof(TCursor), seekPredicate, cursorSelector));

    public RepositoryQueryBuilder<TEntity> CursorPaged<TCursor, TRequest>(
        Expression<Func<TEntity, TCursor, bool>> seekPredicate,
        Expression<Func<TEntity, TRequest, TCursor>> cursorSelector) =>
        RequestCursor(RepositoryCursorDesign.RequestBound(typeof(TCursor), seekPredicate, cursorSelector));

    /// <summary>Validates only supplied continuation cursors, before context acquisition or cache lookup.</summary>
    public RepositoryQueryBuilder<TEntity> ValidateCursor<TCursor>(Expression<Func<TCursor, bool>> predicate, string message)
    {
        design.CursorValidationsList.Add(new RepositoryCursorValidationDesign(predicate, message));
        return this;
    }

    /// <summary>Validates only supplied continuation cursors, before context acquisition or cache lookup.</summary>
    public RepositoryQueryBuilder<TEntity> ValidateCursor<TCursor, TRequest>(
        Expression<Func<TCursor, TRequest, bool>> predicate, string message)
    {
        design.CursorValidationsList.Add(new RepositoryCursorValidationDesign(predicate, message));
        return this;
    }

    private RepositoryQueryBuilder<TEntity> RequestCursor(RepositoryCursorDesign cursor)
    {
        if (design.Source is not null || design.RegisteredSource is not null || design.ResultInputType is not null)
        {
            throw new InvalidOperationException("RGD2701: Configure entity cursor paging before replacement roots or Results(); otherwise configure the cursor on the final result builder.");
        }

        design.Cursor = cursor;
        design.RequirePagingOrder = true;
        return HasResult(RepositoryQueryResultKind.CursorPaged);
    }
}
