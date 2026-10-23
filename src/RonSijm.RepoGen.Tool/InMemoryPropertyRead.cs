using System.Linq.Expressions;

namespace RonSijm.RepoGen.Tool;

/// <summary>Generator-only expression marker. Rendered as a typed read-row member, never emitted or invoked.</summary>
internal static class InMemoryPropertyRead
{
    internal static T Value<T>(object row, string member) => throw new InvalidOperationException("A fixture property read must be rendered, never executed.");

    internal static bool TryRead(MethodCallExpression call, out Expression receiver, out string member)
    {
        if (call.Method.DeclaringType == typeof(InMemoryPropertyRead) && call.Method.Name == nameof(Value) &&
            call.Arguments is [var row, ConstantExpression { Value: string name }])
        {
            receiver = row;
            member = name;
            return true;
        }
        receiver = null!;
        member = "";
        return false;
    }
}
