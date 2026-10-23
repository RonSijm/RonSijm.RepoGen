using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryQueryStageDesign
{
    internal RepositoryQueryStageDesign(RepositoryQueryStageKind kind, Type inputType, LambdaExpression? expression,
        LambdaExpression? condition = null, int? count = null, LambdaExpression? projection = null, RepositoryMembershipDesign? membership = null,
        RepositoryFieldSortDesign? fieldSort = null)
    {
        Kind = kind;
        InputType = inputType;
        Expression = expression;
        Condition = condition;
        Count = count;
        Projection = projection;
        Membership = membership;
        FieldSort = fieldSort;
    }

    public RepositoryQueryStageKind Kind { get; }
    public Type InputType { get; }
    public Type OutputType => Kind == RepositoryQueryStageKind.GroupBy ? Projection!.ReturnType : Kind == RepositoryQueryStageKind.Select ? Expression!.ReturnType : InputType;
    public LambdaExpression? Expression { get; internal set; }
    public LambdaExpression? Condition { get; internal set; }
    public int? Count { get; }
    public LambdaExpression? Projection { get; internal set; }
    public RepositoryMembershipDesign? Membership { get; }
    public RepositoryFieldSortDesign? FieldSort { get; }
}
