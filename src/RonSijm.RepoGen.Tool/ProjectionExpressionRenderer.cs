using System.Globalization;
using System.Linq.Expressions;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class ProjectionExpressionRenderer
{
    public static string Render(LambdaExpression expression, RepositoryTypeNullabilityDesign? resultNullability = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Parameters.Count != 1)
        {
            throw new InvalidOperationException("Repository projections must have exactly one entity parameter.");
        }

        return "static entity => " + RenderBody(expression, ["entity"], resultNullability: resultNullability);
    }

    public static string RenderLambda(LambdaExpression expression, IReadOnlyList<string> parameterNames) =>
        "static (" + string.Join(", ", parameterNames) + ") => " + RenderBody(expression, parameterNames);

    public static string RenderBody(LambdaExpression expression, IReadOnlyList<string> parameterNames,
        IReadOnlyDictionary<ParameterExpression, string>? substitutions = null, RepositoryTypeNullabilityDesign? resultNullability = null)
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
        if (substitutions is not null)
        {
            foreach (var substitution in substitutions)
            {
                parameters.Add(substitution.Key, substitution.Value);
            }
        }

        return RenderExpression(expression.Body, new RenderContext(parameters, CreateTypeAnnotations(resultNullability)));
    }

    private static string RenderExpression(
        Expression expression,
        RenderContext parameters)
    {
        return expression switch
        {
            ParameterExpression parameter when parameters.Names.TryGetValue(parameter, out var name) => name,
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
                parameters.TypeName(typeBinary.TypeOperand, outerNullable: false) + ")",
            NewArrayExpression array when array.NodeType == ExpressionType.NewArrayBounds =>
                "new " + parameters.TypeName(array.Type.GetElementType()!) + "[" +
                string.Join(", ", array.Expressions.Select(item => RenderExpression(item, parameters))) + "]",
            NewArrayExpression array =>
                "new " + parameters.TypeName(array.Type.GetElementType()!) + "[] { " +
                string.Join(", ", array.Expressions.Select(item => RenderExpression(item, parameters))) + " }",
            _ => throw new InvalidOperationException(
                $"Repository expression node '{expression.NodeType}' ({expression.GetType().Name}) is not supported.")
        };
    }

    private static string RenderUnary(
        UnaryExpression unary,
        RenderContext parameters)
    {
        return unary.NodeType switch
        {
            ExpressionType.Convert =>
                "(" + parameters.TypeName(unary.Type) + ")" + RenderExpression(unary.Operand, parameters),
            ExpressionType.ConvertChecked => "checked((" + parameters.TypeName(unary.Type) + ")" + RenderExpression(unary.Operand, parameters) + ")",
            ExpressionType.Not => "!(" + RenderExpression(unary.Operand, parameters) + ")",
            ExpressionType.Negate => "-(" + RenderExpression(unary.Operand, parameters) + ")",
            ExpressionType.NegateChecked => "checked(-(" + RenderExpression(unary.Operand, parameters) + "))",
            ExpressionType.Quote => RenderExpression(unary.Operand, parameters),
            _ => throw new InvalidOperationException(
                $"Repository unary operation '{unary.NodeType}' is not supported.")
        };
    }

    private static string RenderMember(
        MemberExpression member,
        RenderContext parameters)
    {
        if (member.Expression is ConstantExpression)
        {
            throw new InvalidOperationException(
                $"Repository expression member '{member.Member.Name}' captures configuration-time state. " +
                "Pass the value as a filter parameter or use a literal constant so generated code is self-contained.");
        }

        var target = member.Expression is null
            ? parameters.TypeName(member.Member.DeclaringType!, outerNullable: false)
            : RenderReceiver(member.Expression, parameters);
        return target + "." + RenderIdentifier(member.Member.Name);
    }

    private static string RenderNew(
        NewExpression creation,
        RenderContext parameters)
    {
        if (IsAnonymousType(creation.Type) && creation.Members is not null)
        {
            return "new { " + string.Join(", ", creation.Arguments.Select((argument, index) =>
                RenderIdentifier(creation.Members[index].Name) + " = " + RenderExpression(argument, parameters))) + " }";
        }

        return "new " + parameters.TypeName(creation.Type, outerNullable: false) + "(" +
               string.Join(", ", creation.Arguments.Select(argument => RenderExpression(argument, parameters))) + ")";
    }

    internal static bool IsAnonymousType(Type type) => type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false) &&
                                                       type.Name.Contains("AnonymousType", StringComparison.Ordinal);

    private static string RenderMemberInit(
        MemberInitExpression initialization,
        RenderContext parameters)
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
        RenderContext parameters)
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

        var result = "(" + RenderExpression(binary.Left, parameters) + " " + operation + " " + RenderExpression(binary.Right, parameters) + ")";
        return binary.NodeType is ExpressionType.AddChecked or ExpressionType.SubtractChecked or ExpressionType.MultiplyChecked ? "checked(" + result + ")" : result;
    }

    private static string RenderCall(
        MethodCallExpression call,
        RenderContext parameters)
    {
        var methodName = RepositoryModelFactory.EscapeIdentifier(call.Method.Name);
        if (call.Method.IsGenericMethod && !call.Method.GetGenericArguments().Any(ContainsAnonymousType) && !CanInferLinqArguments(call))
        {
            methodName += "<" + string.Join(", ", call.Method.GetGenericArguments()
                .Select(argument => parameters.TypeName(argument))) + ">";
        }

        var target = call.Object is null
            ? parameters.TypeName(call.Method.DeclaringType!, outerNullable: false)
            : RenderReceiver(call.Object, parameters);
        return target + "." + methodName + "(" +
               string.Join(", ", call.Arguments.Select(argument => RenderExpression(argument, parameters))) + ")";
    }

    private static bool ContainsAnonymousType(Type type) => IsAnonymousType(type) || type.GetGenericArguments().Any(ContainsAnonymousType);

    private static string RenderReceiver(Expression expression, RenderContext parameters)
    {
        var source = RenderExpression(expression, parameters);
        return expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } ? "(" + source + ")" : source;
    }

    private static string RenderNestedLambda(
        LambdaExpression lambda,
        RenderContext parameters)
    {
        var nestedParameters = new Dictionary<ParameterExpression, string>(parameters.Names);
        var names = new List<string>();
        for (var index = 0; index < lambda.Parameters.Count; index++)
        {
            var parameter = lambda.Parameters[index];
            var name = RenderIdentifier(parameter.Name ?? "value" + index);
            if (nestedParameters.Values.Contains(name, StringComparer.Ordinal) || ClosedRepositorySourceRenderer.IsInfrastructureParameterName(name) ||
                name.StartsWith("__repoGen", StringComparison.Ordinal))
            {
                name = "__repoGenNested" + nestedParameters.Count.ToString(CultureInfo.InvariantCulture);
            }

            while (nestedParameters.Values.Contains(name, StringComparer.Ordinal))
            {
                name += "_";
            }

            nestedParameters[parameter] = name;
            names.Add(name);
        }

        var parameterList = names.Count == 1 ? names[0] : "(" + string.Join(", ", names) + ")";
        return parameterList + " => " + RenderExpression(lambda.Body, parameters with { Names = nestedParameters });
    }

    private static string RenderIdentifier(string name)
    {
        // Query comprehensions create transparent-identifier parameters/members with names illegal in C# source.
        if (name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(static character => char.IsLetterOrDigit(character) || character == '_'))
        {
            return RepositoryModelFactory.EscapeIdentifier(name);
        }

        return "__repoGenIdentifier" + string.Concat(name.Select(static character => "_" + ((int)character).ToString("X4", CultureInfo.InvariantCulture)));
    }

    internal static bool CanInferLinqArguments(MethodCallExpression call)
    {
        if (call.Method.DeclaringType != typeof(Enumerable) && call.Method.DeclaringType != typeof(Queryable))
        {
            return false;
        }

        var definition = call.Method.GetGenericMethodDefinition();
        var genericParameters = definition.GetGenericArguments();
        var definitionInputs = definition.GetParameters();
        var inputs = call.Method.GetParameters();
        // Retain explicit widening and type-only arguments (Cast, OfType, Empty, etc.).
        return genericParameters.All(argument => definitionInputs.Any(input => ContainsType(input.ParameterType, argument))) &&
               Enumerable.Range(0, inputs.Length).All(index => !definitionInputs[index].ParameterType.ContainsGenericParameters ||
                                                               call.Arguments[index].Type == inputs[index].ParameterType ||
                                                               call.Arguments[index].Type.GetInterfaces().Append(call.Arguments[index].Type.BaseType).Contains(inputs[index].ParameterType));
    }

    private static bool ContainsType(Type type, Type argument) => type == argument ||
                                                                  type.HasElementType && ContainsType(type.GetElementType()!, argument) || type.GetGenericArguments().Any(child => ContainsType(child, argument));

    private static Dictionary<Type, string>? CreateTypeAnnotations(RepositoryTypeNullabilityDesign? shape)
    {
        if (shape is null)
        {
            return null;
        }

        var annotations = new Dictionary<Type, string>();
        var ambiguous = new HashSet<Type>();

        void Visit(RepositoryTypeNullabilityDesign node)
        {
            var name = RepositoryTypeName.Annotated(node);
            if (annotations.TryGetValue(node.Type, out var existing) && existing != name)
            {
                ambiguous.Add(node.Type);
            }
            else
            {
                annotations[node.Type] = name;
            }

            if (node.ElementType is not null)
            {
                Visit(node.ElementType);
            }

            foreach (var child in node.TypeArguments)
            {
                Visit(child);
            }
        }

        Visit(shape);
        foreach (var type in ambiguous)
        {
            annotations.Remove(type);
        }

        return annotations;
    }

    private sealed record RenderContext(Dictionary<ParameterExpression, string> Names, Dictionary<Type, string>? TypeAnnotations)
    {
        public string TypeName(Type type, bool outerNullable = true)
        {
            var name = TypeAnnotations?.TryGetValue(type, out var annotated) == true ? annotated : RepositoryTypeName.WithChildren(type, child => TypeName(child));
            return !outerNullable && !type.IsValueType ? name.TrimEnd('?') : name;
        }
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
