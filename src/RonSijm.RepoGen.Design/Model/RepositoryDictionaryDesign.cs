using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryDictionaryDesign
{
    internal RepositoryDictionaryDesign(LambdaExpression keySelector, LambdaExpression valueSelector,
        RepositoryDuplicateKeyBehavior duplicateKeys = RepositoryDuplicateKeyBehavior.Throw)
    {
        KeySelector = keySelector;
        ValueSelector = valueSelector;
        DuplicateKeys = duplicateKeys;
    }

    public LambdaExpression KeySelector { get; internal set; }
    public LambdaExpression ValueSelector { get; internal set; }
    public RepositoryDuplicateKeyBehavior DuplicateKeys { get; }
}
