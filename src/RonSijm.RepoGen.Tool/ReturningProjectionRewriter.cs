using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Replaces approved EF.Property reads with typed detached-buffer values, never tracker entries.</summary>
internal sealed class ReturningProjectionRewriter(IEntityType entity, ParameterExpression row, bool includeMappedProperties = false) : ExpressionVisitor
{
    internal Dictionary<ParameterExpression, string> Substitutions { get; } = [];

    protected override Expression VisitMember(MemberExpression node) => includeMappedProperties && node.Expression == row && Member(entity, node) is { } property
        ? Read(property) : base.VisitMember(node);

    internal static IProperty? Member(IEntityType entity, MemberExpression member) =>
        entity.GetProperties().SingleOrDefault(property => property.PropertyInfo == member.Member || property.FieldInfo == member.Member);

    private ParameterExpression Read(IProperty property)
    {
        var value = Expression.Parameter(property.ClrType, "__repoGenReturnedValue" + Substitutions.Count);
        Substitutions.Add(value, "__repoGenReturnedRow.GetValue<" + RepositoryModelFactory.CSharpTypeName(property.ClrType) + ">(" + JsonSerializer.Serialize(property.Name) + ")");
        return value;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (!IsProperty(node)) return base.VisitMethodCall(node);
        var property = Property(entity, row, node);
        return Read(property);
    }

    internal static bool IsProperty(MethodCallExpression node) => node.Method.DeclaringType == typeof(EF) && node.Method.Name == nameof(EF.Property);

    internal static IProperty Property(IEntityType entity, ParameterExpression row, MethodCallExpression call)
    {
        Expression instance = call.Arguments[0];
        while (instance is UnaryExpression { NodeType: ExpressionType.Convert, Method: null } conversion) instance = conversion.Operand;
        if (instance != row || call.Arguments[1] is not ConstantExpression { Value: string name } ||
            entity.FindProperty(name) is not { } property || property.ClrType != call.Type)
            throw new InvalidOperationException("RGD1306: Returned EF.Property reads require this entity, a literal mapped scalar property name and its exact model CLR type.");
        return property;
    }
}
