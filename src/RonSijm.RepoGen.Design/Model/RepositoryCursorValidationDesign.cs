using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

/// <summary>A structural continuation guard. It does not authenticate or sign a cursor.</summary>
public sealed class RepositoryCursorValidationDesign
{
    internal RepositoryCursorValidationDesign(LambdaExpression predicate, string message)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A cursor validation message is required.", nameof(message));
        }

        Message = message;
    }

    public LambdaExpression Predicate { get; internal set; }
    public string Message { get; }
}
