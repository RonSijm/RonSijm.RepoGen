using System.Globalization;
using System.Linq.Expressions;

namespace RonSijm.RepoGen.Tool;

internal static class ProjectionExpressionRenderer
{
    public static string Render(LambdaExpression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Parameters.Count != 1)
        {
            throw new InvalidOperationException("Repository projections must have exactly one entity parameter.");
        }

        return "static entity => " + RenderBody(expression, ["entity"]);
    }

    public static string RenderLambda(LambdaExpression expression, IReadOnlyList<string> parameterNames) =>
        "static (" + string.Join(", ", parameterNames) + ") => " + RenderBody(expression, parameterNames);

    public static string RenderBody(LambdaExpression expression, IReadOnlyList<string> parameterNames)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(parameterNames);
        if (expression.Parameters.Count != parameterNames.Count)
        {
            throw new InvalidOperationException(
                $"Expression has {expression.Parameters.Count} parameters but {parameterNames.Count} generated names were supplied.");
        }

        var parameters = expression.Parameters
            .Select((parameter, index) => new { parameter, Name = parameterNames[index] })
            .ToDictionary(static item => item.parameter, static item => item.Name);
        return RenderExpression(expression.Body, parameters);
    }

    private static string RenderExpression(
        Expression expression,
        IReadOnlyDictionary<ParameterExpression, string> parameters)
    {
        return expression switch
        {
            ParameterExpression parameter when parameters.TryGetValue(parameter, out var name) => name,
            MemberExpression member => RenderMember(member, parameters),
            NewExpression creation => RenderNew(creation, parameters),
            MemberInitExpression initialization => RenderMemberInit(initialization, parameters),
            UnaryExpression unary => RenderUnary(unary, parameters),
            BinaryExpression binary => RenderBinary(binary, parameters),
            ConditionalExpression conditional =>
                "(" + RenderExpression(conditional.Test, parameters) + " ? " +
                RenderExpression(conditional.IfTrue, parameters) + " : " +
                RenderExpression(conditional.IfFalse, parameters) + ")",
            MethodCallExpression call => RenderCall(call, parameters),
            ConstantExpression constant => RenderConstant(constant),
            LambdaExpression lambda => RenderNestedLambda(lambda, parameters),
            TypeBinaryExpression typeBinary when typeBinary.NodeType == ExpressionType.TypeIs =>
                "(" + RenderExpression(typeBinary.Expression, parameters) + " is " +
                RepositoryModelFactory.CSharpTypeName(typeBinary.TypeOperand) + ")",
            NewArrayExpression array =>
                "new " + RepositoryModelFactory.CSharpTypeName(array.Type.GetElementType()!) + "[] { " +
                string.Join(", ", array.Expressions.Select(item => RenderExpression(item, parameters))) + " }",
            _ => throw new InvalidOperationException(
                $"Repository expression node '{expression.NodeType}' ({expression.GetType().Name}) is not supported.")
        };
    }

    private static string RenderUnary(
        UnaryExpression unary,
        IReadOnlyDictionary<ParameterExpression, string> parameters)
    {
        return unary.NodeType switch
        {
            ExpressionType.Convert or ExpressionType.ConvertChecked =>
                "(" + RepositoryModelFactory.CSharpTypeName(unary.Type) + ")" + RenderExpression(unary.Operand, parameters),
            ExpressionType.Not => "!(" + RenderExpression(unary.Operand, parameters) + ")",
            ExpressionType.Negate or ExpressionType.NegateChecked => "-(" + RenderExpression(unary.Operand, parameters) + ")",
            ExpressionType.Quote => RenderExpression(unary.Operand, parameters),
            _ => throw new InvalidOperationException(
                $"Repository unary operation '{unary.NodeType}' is not supported.")
        };
    }

    private static string RenderMember(
        MemberExpression member,
        IReadOnlyDictionary<ParameterExpression, string> parameters)
    {
        if (member.Expression is ConstantExpression)
        {
            throw new InvalidOperationException(
                $"Repository expression member '{member.Member.Name}' captures configuration-time state. " +
                "Pass the value as a filter parameter or use a literal constant so generated code is self-contained.");
        }

        var target = member.Expression is null
            ? RepositoryModelFactory.CSharpTypeName(member.Member.DeclaringType!)
            : RenderExpression(member.Expression, parameters);
        return target + "." + RepositoryModelFactory.EscapeIdentifier(member.Member.Name);
    }

    private static string RenderNew(
        NewExpression creation,
        IReadOnlyDictionary<ParameterExpression, string> parameters)
    {
        return "new " + RepositoryModelFactory.CSharpTypeName(creation.Type) + "(" +
               string.Join(", ", creation.Arguments.Select(argument => RenderExpression(argument, parameters))) + ")";
    }

    private static string RenderMemberInit(
        MemberInitExpression initialization,
        IReadOnlyDictionary<ParameterExpression, string> parameters)
    {
        var assignments = initialization.Bindings.Select(binding => binding switch
        {
            MemberAssignment assignment =>
                RepositoryModelFactory.EscapeIdentifier(assignment.Member.Name) + " = " +
                RenderExpression(assignment.Expression, parameters),
            _ => throw new InvalidOperationException(
                $"Repository member binding '{binding.BindingType}' is not supported for '{binding.Member.Name}'.")
        });

        return RenderNew(initialization.NewExpression, parameters) + " { " +
               string.Join(", ", assignments) + " }";
    }

    private static string RenderBinary(
        BinaryExpression binary,
        IReadOnlyDictionary<ParameterExpression, string> parameters)
    {
        var operation = binary.NodeType switch
        {
            ExpressionType.Add or ExpressionType.AddChecked => "+",
            ExpressionType.Subtract or ExpressionType.SubtractChecked => "-",
            ExpressionType.Multiply or ExpressionType.MultiplyChecked => "*",
            ExpressionType.Divide => "/",
            ExpressionType.Modulo => "%",
            ExpressionType.And => "&",
            ExpressionType.AndAlso => "&&",
            ExpressionType.Or => "|",
            ExpressionType.OrElse => "||",
            ExpressionType.ExclusiveOr => "^",
            ExpressionType.Equal => "==",
            ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.Coalesce => "??",
            _ => throw new InvalidOperationException(
                $"Repository binary operation '{binary.NodeType}' is not supported.")
        };

        return "(" + RenderExpression(binary.Left, parameters) + " " + operation + " " +
               RenderExpression(binary.Right, parameters) + ")";
    }

    private static string RenderCall(
        MethodCallExpression call,
        IReadOnlyDictionary<ParameterExpression, string> parameters)
    {
        var methodName = RepositoryModelFactory.EscapeIdentifier(call.Method.Name);
        if (call.Method.IsGenericMethod)
        {
            methodName += "<" + string.Join(", ", call.Method.GetGenericArguments()
                .Select(static argument => RepositoryModelFactory.CSharpTypeName(argument))) + ">";
        }

        var target = call.Object is null
            ? RepositoryModelFactory.CSharpTypeName(call.Method.DeclaringType!)
            : RenderExpression(call.Object, parameters);
        return target + "." + methodName + "(" +
               string.Join(", ", call.Arguments.Select(argument => RenderExpression(argument, parameters))) + ")";
    }

    private static string RenderNestedLambda(
        LambdaExpression lambda,
        IReadOnlyDictionary<ParameterExpression, string> parameters)
    {
        var nestedParameters = new Dictionary<ParameterExpression, string>(parameters);
        var names = new List<string>();
        for (var index = 0; index < lambda.Parameters.Count; index++)
        {
            var parameter = lambda.Parameters[index];
            var name = RepositoryModelFactory.EscapeIdentifier(parameter.Name ?? "value" + index);
            nestedParameters[parameter] = name;
            names.Add(name);
        }

        var parameterList = names.Count == 1 ? names[0] : "(" + string.Join(", ", names) + ")";
        return parameterList + " => " + RenderExpression(lambda.Body, nestedParameters);
    }

    private static string RenderConstant(ConstantExpression constant)
    {
        if (constant.Value is null)
        {
            return "null";
        }

        return constant.Value switch
        {
            string value => "\"" + EscapeString(value) + "\"",
            char value => "'" + EscapeCharacter(value) + "'",
            bool value => value ? "true" : "false",
            byte value => value.ToString(CultureInfo.InvariantCulture),
            sbyte value => value.ToString(CultureInfo.InvariantCulture),
            short value => value.ToString(CultureInfo.InvariantCulture),
            ushort value => value.ToString(CultureInfo.InvariantCulture),
            int value => value.ToString(CultureInfo.InvariantCulture),
            uint value => value.ToString(CultureInfo.InvariantCulture) + "U",
            long value => value.ToString(CultureInfo.InvariantCulture) + "L",
            ulong value => value.ToString(CultureInfo.InvariantCulture) + "UL",
            float value => value.ToString("R", CultureInfo.InvariantCulture) + "F",
            double value => value.ToString("R", CultureInfo.InvariantCulture) + "D",
            decimal value => value.ToString(CultureInfo.InvariantCulture) + "M",
            Enum value => RepositoryModelFactory.CSharpTypeName(value.GetType()) + "." + value,
            _ => throw new InvalidOperationException(
                $"Repository constant type '{constant.Value.GetType().FullName}' is not supported.")
        };
    }

    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private static string EscapeCharacter(char value) => value switch
    {
        '\\' => "\\\\",
        '\'' => "\\'",
        '\r' => "\\r",
        '\n' => "\\n",
        '\t' => "\\t",
        _ => value.ToString()
    };
}