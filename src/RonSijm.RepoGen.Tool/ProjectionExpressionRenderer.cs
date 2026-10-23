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
            ListInitExpression initialization => RenderListInit(initialization, parameters),
            UnaryExpression unary => RenderUnary(unary, parameters),
            BinaryExpression binary => RenderBinary(binary, parameters),
            ConditionalExpression conditional =>
                "(" + RenderExpression(conditional.Test, parameters) + " ? " +
                RenderExpression(conditional.IfTrue, parameters) + " : " +
                RenderExpression(conditional.IfFalse, parameters) + ")",
            MethodCallExpression call => RenderCall(call, parameters),
            IndexExpression { Object: { } receiver } index => RenderReceiver(receiver, parameters) + "[" +
                string.Join(", ", index.Arguments.Select(argument => RenderExpression(argument, parameters))) + "]!",
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
            ExpressionType.Convert => RenderConversion(unary, parameters),
            ExpressionType.ConvertChecked => "checked(" + RenderConversion(unary, parameters) + ")",
            ExpressionType.Not => "!(" + RenderExpression(unary.Operand, parameters) + ")",
            ExpressionType.Negate => "-(" + RenderExpression(unary.Operand, parameters) + ")",
            ExpressionType.NegateChecked => "checked(-(" + RenderExpression(unary.Operand, parameters) + "))",
            ExpressionType.Quote => RenderExpression(unary.Operand, parameters),
            _ => throw new InvalidOperationException(
                $"Repository unary operation '{unary.NodeType}' is not supported.")
        };
    }

    private static string RenderConversion(UnaryExpression unary, RenderContext parameters)
    {
        var operand = RenderExpression(unary.Operand, parameters);
        // A qualified type followed by a sign can parse as subtraction instead of a cast.
        if (operand.StartsWith('-') || operand.StartsWith('+'))
        {
            operand = "(" + operand + ")";
        }

        return "(" + parameters.TypeName(unary.Type) + ")" + operand;
    }

    private static string RenderMember(
        MemberExpression member,
        RenderContext parameters)
    {
        if (member.Expression is ConstantExpression { Value: not string })
        {
            throw new InvalidOperationException(
                $"Repository expression member '{member.Member.Name}' captures configuration-time state. " +
                "Pass the value as a filter parameter or use a literal constant so generated code is self-contained.");
        }

        var target = member.Expression is null
            ? parameters.TypeName(member.Member.DeclaringType!, outerNullable: false)
            : RenderReceiver(member.Expression, parameters);
        if (member.Member.Name == nameof(Nullable<int>.Value) && member.Expression is not null && Nullable.GetUnderlyingType(member.Expression.Type) is not null)
        {
            // Expression trees omit the source's null-forgiving annotation. Preserve explicit Value access.
            target += "!";
        }
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

    private static string RenderListInit(ListInitExpression initialization, RenderContext parameters)
    {
        var elements = initialization.Initializers.Select(initializer => initializer.Arguments.Count == 1
            ? RenderExpression(initializer.Arguments[0], parameters)
            : "{ " + string.Join(", ", initializer.Arguments.Select(argument => RenderExpression(argument, parameters))) + " }");
        return RenderNew(initialization.NewExpression, parameters) + " { " + string.Join(", ", elements) + " }";
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
        if (call.Object is not null && call.Method.IsSpecialName && call.Arguments.Count > 0 &&
            call.Method.DeclaringType!.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Any(property => property.GetMethod == call.Method && property.GetIndexParameters().Length == call.Arguments.Count))
        {
            // Expression trees erase source null-forgiving annotations on explicit indexer casts.
            return RenderReceiver(call.Object, parameters) + "[" + string.Join(", ", call.Arguments.Select(argument => RenderExpression(argument, parameters))) + "]!";
        }
        if (InMemoryPropertyRead.TryRead(call, out var receiver, out var member))
        {
            return RenderReceiver(receiver, parameters) + "." + RenderIdentifier(member);
        }
        // C# 14 binds array membership to MemoryExtensions through a span conversion.
        // Keep the generated expression compatible with older C# and EF query visitors.
        if (call.Method.DeclaringType == typeof(MemoryExtensions) && call.Method.Name == nameof(MemoryExtensions.Contains) &&
            call.Method.IsGenericMethod && call.Arguments.Count is 2 or 3 && ReadOnlySpanArray(call.Arguments[0]) is { } array)
        {
            return "global::System.Linq.Enumerable.Contains<" + parameters.TypeName(call.Method.GetGenericArguments()[0]) + ">(" +
                RenderExpression(array, parameters) + ", " + string.Join(", ", call.Arguments.Skip(1).Select(argument => RenderExpression(argument, parameters))) + ")";
        }

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

    private static Expression? ReadOnlySpanArray(Expression expression)
    {
        if (!expression.Type.IsGenericType || expression.Type.GetGenericTypeDefinition() != typeof(ReadOnlySpan<>))
        {
            return null;
        }

        var array = expression switch
        {
            MethodCallExpression { Object: null, Arguments.Count: 1 } conversion when
                conversion.Method.Name == "op_Implicit" && conversion.Method.DeclaringType == expression.Type => conversion.Arguments[0],
            UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion when
                conversion.Method?.Name == "op_Implicit" && conversion.Method.DeclaringType == expression.Type => conversion.Operand,
            _ => null
        };
        return array?.Type.IsSZArray == true && array.Type.GetElementType() == expression.Type.GetGenericArguments()[0] ? array : null;
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
