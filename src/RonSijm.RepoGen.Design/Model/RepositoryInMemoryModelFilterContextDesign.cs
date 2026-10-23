using System.Linq.Expressions;
using System.Reflection;

namespace RonSijm.RepoGen.Design;

/// <summary>An explicit fixture-only binding from a context member to a typed partition key value.</summary>
public sealed class RepositoryInMemoryModelFilterContextDesign
{
    internal RepositoryInMemoryModelFilterContextDesign(MemberInfo member, LambdaExpression contextValue, LambdaExpression keyValue)
    {
        Member = member;
        ContextValue = contextValue;
        KeyValue = keyValue;
    }

    public MemberInfo Member { get; }
    public LambdaExpression ContextValue { get; }
    public LambdaExpression KeyValue { get; }
    public Type ContextType => ContextValue.Parameters[0].Type;
    public Type ContextKeyType => KeyValue.Parameters[0].Type;
}
