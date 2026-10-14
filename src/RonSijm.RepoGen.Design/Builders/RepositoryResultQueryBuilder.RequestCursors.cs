using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryResultQueryBuilder<TResult>
{
    public RepositoryResultQueryBuilder<TResult> CursorPaged<TCursor, TRequest>(
        Expression<Func<TResult, TCursor, TRequest, bool>> seekPredicate,
        Expression<Func<TResult, TCursor>> cursorSelector) =>
        RequestCursor(RepositoryCursorDesign.RequestBound(typeof(TCursor), seekPredicate, cursorSelector));

    /// <summary>The seek request's parameter name is also used by the cursor mapper.</summary>
    public RepositoryResultQueryBuilder<TResult> CursorPaged<TCursor, TRequest>(
        Expression<Func<TResult, TCursor, TRequest, bool>> seekPredicate,
        Expression<Func<TResult, TRequest, TCursor>> cursorSelector) =>
        RequestCursor(RepositoryCursorDesign.RequestBound(typeof(TCursor), seekPredicate, cursorSelector));

    public RepositoryResultQueryBuilder<TResult> CursorPaged<TCursor, TRequest>(
        Expression<Func<TResult, TCursor, bool>> seekPredicate,
        Expression<Func<TResult, TRequest, TCursor>> cursorSelector) =>
        RequestCursor(RepositoryCursorDesign.RequestBound(typeof(TCursor), seekPredicate, cursorSelector));

    /// <summary>Validates only supplied continuation cursors, before context acquisition or cache lookup.</summary>
    public RepositoryResultQueryBuilder<TResult> ValidateCursor<TCursor>(Expression<Func<TCursor, bool>> predicate, string message)
    {
        RequireCurrentStage();
        design.CursorValidationsList.Add(new RepositoryCursorValidationDesign(predicate, message));
        return this;
    }

    /// <summary>Validates only supplied continuation cursors, before context acquisition or cache lookup.</summary>
    public RepositoryResultQueryBuilder<TResult> ValidateCursor<TCursor, TRequest>(
        Expression<Func<TCursor, TRequest, bool>> predicate, string message)
    {
        RequireCurrentStage();
        design.CursorValidationsList.Add(new RepositoryCursorValidationDesign(predicate, message));
        return this;
    }

    private RepositoryResultQueryBuilder<TResult> RequestCursor(RepositoryCursorDesign cursor)
    {
        Terminal(RepositoryQueryResultKind.CursorPaged);
        cursor.OnResults = true;
        design.Cursor = cursor;
        design.RequirePagingOrder = true;
        return this;
    }
}
