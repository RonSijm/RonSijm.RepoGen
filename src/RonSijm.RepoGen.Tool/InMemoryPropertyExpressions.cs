using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Exact, read-only model-property lowering. No reflection lookup survives into generated tests.</summary>
internal sealed class InMemoryPropertyExpressions
{
    private readonly Dictionary<(Type Entity, string Property, Type Value), InMemoryPropertyModel> bindings = [];
    private readonly Dictionary<(Type Entity, MemberInfo Member), (Type Type, InMemoryPropertyModel Mapping)> members = [];
    private readonly Dictionary<(Type Entity, string Property), (IProperty Property, InMemoryPropertyModel Mapping)> indexers = [];
    private readonly HashSet<InMemoryPropertyReadModel> used = [];
    private static readonly MethodInfo Read = typeof(InMemoryPropertyRead).GetMethod(nameof(InMemoryPropertyRead.Value), BindingFlags.Static | BindingFlags.NonPublic)!;

    internal InMemoryPropertyExpressions(IModel model, RepositoryDesign design)
    {
        foreach (var definition in design.Entities.Where(static entity => entity.RepositoryEnabled && entity.TestDoubleEnabled != false &&
            (entity.ReadEnabled || entity.WriteEnabled) && entity.InMemoryStore?.PropertyMappings.Count > 0))
        {
            var entity = model.FindEntityType(definition.EntityType) ?? throw new InvalidOperationException("RGD4901: Fixture scalar values require a mapped entity.");
            Add(entity, RepositoryModelFactory.CreateInMemoryProperties(entity, definition.InMemoryStore!));
        }
    }

    internal InMemoryPropertyExpressions(IEntityType entity, IReadOnlyList<InMemoryPropertyModel> properties) => Add(entity, properties);

    internal InMemoryPropertyExpressions(IEnumerable<(IEntityType Entity, IReadOnlyList<InMemoryPropertyModel> Properties)> participants)
    {
        foreach (var (entity, properties) in participants)
        {
            Add(entity, properties);
        }
    }

    private void Add(IEntityType entity, IReadOnlyList<InMemoryPropertyModel> properties)
    {
        foreach (var property in properties)
        {
            var mapped = entity.FindProperty(property.PropertyName)!;
            bindings.Add((entity.ClrType, property.PropertyName, mapped.ClrType), property);
            if (mapped.IsIndexerProperty())
            {
                indexers.Add((entity.ClrType, property.PropertyName), (mapped, property));
            }
            else
            {
                foreach (var member in new MemberInfo?[] { mapped.PropertyInfo, mapped.FieldInfo }.OfType<MemberInfo>())
                {
                    members.Add((entity.ClrType, member), (mapped.ClrType, property));
                }
            }
        }
    }

    internal IReadOnlyList<InMemoryPropertyReadModel> Receipts => used.OrderBy(static read => read.EntityTypeName, StringComparer.Ordinal)
        .ThenBy(static read => read.PropertyName, StringComparer.Ordinal).ToArray();

    internal MethodCallExpression? Rewrite(MethodCallExpression node, Func<Expression, Expression> visit)
    {
        if (node.Method.DeclaringType != typeof(EF) || node.Method.Name != nameof(EF.Property) || !node.Method.IsGenericMethod ||
            node.Arguments is not [var receiver, ConstantExpression { Value: string name }] ||
            !bindings.TryGetValue((receiver.Type, name, node.Type), out var property))
        {
            return Index(node, visit);
        }
        return Value(receiver, node.Type, property, visit);
    }

    internal MethodCallExpression? Rewrite(MemberExpression node, Func<Expression, Expression> visit) => node.Expression is { } receiver &&
        members.TryGetValue((receiver.Type, node.Member), out var binding) && binding.Type == node.Type
            ? Value(receiver, node.Type, binding.Mapping, visit) : null;

    internal MethodCallExpression? Rewrite(UnaryExpression node, Func<Expression, Expression> visit) => node is { NodeType: ExpressionType.Convert, Method: null } &&
        Indexed(node.Operand) is { } indexed && node.Type == indexed.Property.ClrType
            ? Value(indexed.Receiver, node.Type, indexed.Mapping, visit) : null;

    internal MethodCallExpression? Rewrite(IndexExpression node, Func<Expression, Expression> visit) => Index(node, visit);

    private MethodCallExpression? Index(Expression node, Func<Expression, Expression> visit) => Indexed(node) is { } indexed && node.Type == indexed.Property.ClrType
        ? Value(indexed.Receiver, node.Type, indexed.Mapping, visit) : null;

    private (Expression Receiver, IProperty Property, InMemoryPropertyModel Mapping)? Indexed(Expression node)
    {
        Expression? receiver;
        Expression? argument;
        MemberInfo? member;
        switch (node)
        {
            case MethodCallExpression { Object: { } instance, Arguments: [var key] } call:
                receiver = instance;
                argument = key;
                member = call.Method;
                break;
            case IndexExpression { Object: { } instance, Arguments: [var key] } index:
                receiver = instance;
                argument = key;
                member = index.Indexer;
                break;
            default:
                return null;
        }
        return argument is ConstantExpression { Value: string name } && indexers.TryGetValue((receiver.Type, name), out var binding) &&
            (member == binding.Property.PropertyInfo || member == binding.Property.PropertyInfo?.GetMethod)
                ? (receiver, binding.Property, binding.Mapping) : null;
    }

    private MethodCallExpression Value(Expression receiver, Type type, InMemoryPropertyModel property, Func<Expression, Expression> visit)
    {
        used.Add(new(RepositoryModelFactory.CSharpTypeName(receiver.Type), property.PropertyName, property.TypeName, property.AccessKind));
        return Expression.Call(Read.MakeGenericMethod(type), visit(receiver), Expression.Constant(property.MemberName));
    }
}
