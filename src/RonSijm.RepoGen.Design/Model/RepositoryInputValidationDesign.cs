using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryInputValidationDesign
{
    internal RepositoryInputValidationDesign(LambdaExpression predicate, string message)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A validation message is required.", nameof(message));
        }

        Message = message;
    }

    public LambdaExpression Predicate { get; internal set; }
    public string Message { get; }
}
