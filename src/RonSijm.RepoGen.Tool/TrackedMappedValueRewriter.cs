using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Exact current-row scalar reads through EF entry access, without emitting direct CLR getter calls.</summary>
internal sealed class TrackedMappedValueRewriter(IEntityType entity, ParameterExpression row, string rowName = "entity") : ExpressionVisitor
{
    internal Dictionary<ParameterExpression, string> Substitutions { get; } = [];

    protected override Expression VisitMember(MemberExpression node) => node.Expression == row && ReturningProjectionRewriter.Member(entity, node) is { } property
        ? Read(property) : base.VisitMember(node);

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node is { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked, Method: null,
                Operand: MethodCallExpression call } && IsIndexer(call))
        {
            var property = Indexer(call);
            if (node.Type != property.ClrType)
                throw new InvalidOperationException("RGD1312: Tracked indexer reads require the exact mapped scalar CLR type.");
            // Do not round-trip a nullable value through a non-nullable object/string cast.
            return Read(property);
        }
        return base.VisitUnary(node);
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (ReturningProjectionRewriter.IsProperty(node))
        {
            Expression instance = node.Arguments[0];
            while (instance is UnaryExpression { NodeType: ExpressionType.Convert, Method: null } conversion) instance = conversion.Operand;
            if (instance != row || node.Arguments[1] is not ConstantExpression { Value: string name } ||
                entity.FindProperty(name) is not { } property || property.ClrType != node.Type)
                throw new InvalidOperationException("RGD1312: Tracked mapped setter reads require this entity, a literal mapped scalar name and its exact CLR type.");
            return Read(property);
        }
        if (IsIndexer(node))
        {
            var property = Indexer(node);
            var value = Read(property);
            return value.Type == node.Type ? value : Expression.Convert(value, node.Type);
        }
        return base.VisitMethodCall(node);
    }

    private IProperty Indexer(MethodCallExpression node)
    {
        if (node.Object != row || node.Arguments is not [ConstantExpression { Value: string name }] ||
            entity.FindProperty(name) is not { } property || !property.IsIndexerProperty() || property.PropertyInfo?.GetMethod != node.Method)
            throw new InvalidOperationException("RGD1312: Tracked indexer reads require a literal mapped scalar name on this entity.");
        return property;
    }

    private bool IsIndexer(MethodCallExpression node) => node.Method.Name == "get_Item" &&
        entity.GetProperties().Any(property => property.IsIndexerProperty() && property.PropertyInfo?.GetMethod == node.Method);

    private ParameterExpression Read(IProperty property)
    {
        var value = Expression.Parameter(property.ClrType, "__repoGenMappedCurrent" + Substitutions.Count);
        Substitutions.Add(value, "context.Entry(" + rowName + ").Property<" + RepositoryModelFactory.CSharpTypeName(property.ClrType, property.IsNullable) + ">(" +
            JsonSerializer.Serialize(property.Name) + ").CurrentValue");
        return value;
    }
}
