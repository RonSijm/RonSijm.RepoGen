using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryCursorDesign
{
    internal RepositoryCursorDesign(
        Type cursorType,
        LambdaExpression seekPredicate,
        LambdaExpression cursorSelector)
    {
        CursorType = cursorType;
        SeekPredicate = seekPredicate;
        CursorSelector = cursorSelector;
    }

    public Type CursorType { get; }
    public bool OnResults { get; internal set; }
    internal bool IsRequestBound => SeekPredicate.Parameters.Count == 3 || CursorSelector.Parameters.Count == 2;

    internal static RepositoryCursorDesign RequestBound(Type cursorType, LambdaExpression seekPredicate, LambdaExpression cursorSelector)
    {
        if (seekPredicate is null)
        {
            throw new ArgumentNullException(nameof(seekPredicate));
        }

        if (cursorSelector is null)
        {
            throw new ArgumentNullException(nameof(cursorSelector));
        }

        if (seekPredicate.Parameters.Count == 3 && cursorSelector.Parameters.Count == 2)
        {
            cursorSelector = RepositoryFragmentExpressions.BindRequest(cursorSelector, seekPredicate.Parameters[2]);
        }

        return new RepositoryCursorDesign(cursorType, seekPredicate, cursorSelector);
    }

    public LambdaExpression SeekPredicate { get; internal set; }

    public LambdaExpression CursorSelector { get; internal set; }
}
