using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Test-only exact-call expansion. Never evaluates a call, captured field or property getter.</summary>
internal sealed class InMemoryTranslationExpressions : ExpressionVisitor
{
    private readonly Dictionary<MethodInfo, RepositoryInMemoryTranslationDesign> definitions = [];
    private readonly HashSet<RepositoryInMemoryTranslationDesign> expanding = [];
    private readonly HashSet<RepositoryInMemoryTranslationDesign> used = [];
    private readonly InMemoryPropertyExpressions? propertyMappings;

    internal InMemoryTranslationExpressions(IEnumerable<RepositoryInMemoryTranslationDesign> declarations, IModel? model = null,
        RepositoryDesign? readDesign = null, InMemoryPropertyExpressions? propertyBindings = null)
    {
        propertyMappings = propertyBindings;
        if (propertyMappings is null && model is not null && readDesign is not null)
        {
            propertyMappings = new(model, readDesign);
        }
        var nestedShapes = new Dictionary<RepositoryInMemoryTranslationDesign, InMemoryProjectionDataShape>();
        foreach (var declaration in declarations)
        {
            if (declaration.IsProjection && declaration.AllowNestedData)
            {
                var shape = InMemoryProjectionDataShape.Create(declaration.Call.ReturnType, model) ??
                    throw new InvalidOperationException("RGD4601: Nested projection translations require independent public DTO data with readable scalar/DTO/array/list members and final-model entity exclusion. Recursive types, entities/services, unsupported collections and shapes beyond 16 levels or 128 DTO types are not supported.");
                nestedShapes.Add(declaration, shape);
            }
            else if (declaration.IsProjection && !InMemoryProjectionTranslationInspector.FlatDto(declaration.Call.ReturnType, model))
                throw new InvalidOperationException("RGD4601: Projection translations require an independent public flat DTO result with readable scalar members and the final EF model for entity exclusion. Scalar results, entity/value-object graphs, framework/service/collection types are not DTO projection declarations.");
            if (declaration.Call.Body is not MethodCallExpression call || !call.Method.IsPublic ||
                call.Method.ContainsGenericParameters || call.Method.ReturnType.IsByRef ||
                call.Method.GetParameters().Any(static parameter => parameter.ParameterType.IsByRef) || !declaration.IsProjection && !Scalar(call.Type) ||
                declaration.Call.Parameters.Any(parameter => !Scalar(parameter.Type)) ||
                declaration.Call.Parameters.Select(static parameter => parameter.Type)
                    .SequenceEqual(declaration.Replacement.Parameters.Select(static parameter => parameter.Type)) == false ||
                declaration.Call.ReturnType != declaration.Replacement.ReturnType)
                throw new InvalidOperationException("RGD4001: In-memory translations require one public closed method call with matching public scalar inputs and the declared scalar/DTO result type.");
            var slots = call.Arguments.Concat(call.Object is null ? [] : new[] { call.Object }).ToArray();
            if (declaration.Call.Parameters.Any(parameter => slots.Count(slot => slot == parameter) != 1) ||
                slots.Any(slot => !declaration.Call.Parameters.Any(parameter => parameter == slot) && !FixedShape(slot)))
                throw new InvalidOperationException("RGD4001: Each translation input must appear exactly once as a direct call argument or receiver; other slots must be fixed literals/static member symbols, never computed inputs or captured instances.");
            if (!definitions.TryAdd(call.Method, declaration))
                throw new InvalidOperationException("RGD4001: A closed method overload may have only one in-memory translation.");
        }
        var dtoTypes = definitions.Values.Where(static declaration => declaration.IsProjection)
            .Select(static declaration => Nullable.GetUnderlyingType(declaration.Call.ReturnType) ?? declaration.Call.ReturnType)
            .Where(type => InMemoryProjectionTranslationInspector.FlatDto(type, model)).ToHashSet();
        foreach (var declaration in definitions.Values)
        {
            var replacement = Expand(declaration.Replacement);
            var reasons = new SortedSet<string>(StringComparer.Ordinal);
            var allowedDtos = declaration.AllowNestedData ? dtoTypes.Concat(nestedShapes[declaration].Dtos).ToHashSet() : dtoTypes;
            new InMemoryQueryExpressionInspector(reasons).Visit(replacement);
            new InMemoryTranslationReplacementInspector(reasons, declaration.IsProjection ? allowedDtos : null, declaration.AllowNestedData).Visit(replacement);
            if (declaration.IsProjection)
            {
                if (!InMemoryProjectionTranslationInspector.ConstructsResult(replacement.Body, replacement.ReturnType))
                    reasons.Add("Return the declared DTO through a constructor/initializer, optionally a conditional nullable result.");
                if (declaration.AllowNestedData) new InMemoryNestedProjectionTranslationInspector(reasons, allowedDtos).Visit(replacement);
                else new InMemoryProjectionTranslationInspector(reasons, dtoTypes).Visit(replacement);
            }
            var usage = new InMemoryTranslationParameterUsage();
            usage.Visit(replacement.Body);
            if (replacement.Parameters.Any(parameter => !usage.Parameters.Contains(parameter)))
                reasons.Add("Every input must remain referenced so scalar/member coverage cannot be hidden by a replacement.");
            if (reasons.Count != 0)
                throw new InvalidOperationException((declaration.IsProjection ? "RGD4602" : "RGD4002") + ": Translation '" + declaration.Name + "' requires self-contained portable replacement semantics: " + string.Join(" ", reasons));
        }
        used.Clear();
    }

    internal LambdaExpression Expand(LambdaExpression expression) => (LambdaExpression)Visit(expression)!;
    internal IReadOnlyList<InMemoryPropertyReadModel> PropertyReads => propertyMappings?.Receipts ?? [];
    internal IReadOnlyList<InMemoryTranslationReceiptModel> Receipts => used.OrderBy(static definition => definition.Name, StringComparer.Ordinal)
        .Select(static definition => new InMemoryTranslationReceiptModel(definition.Name,
            definition.Call.Body is MethodCallExpression call ? call.Method.DeclaringType!.FullName + "." + call.Method : "",
            definition.IsProjection ? definition.AllowNestedData ? "ExplicitNestedDtoAndMaterializedCollectionConstruction" : "ExplicitFlatDtoConstruction" : null)).ToArray();

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (propertyMappings?.Rewrite(node, expression => Visit(expression)!) is { } mapped)
        {
            return mapped;
        }
        if (!definitions.TryGetValue(node.Method, out var declaration)) return base.VisitMethodCall(node);
        var template = (MethodCallExpression)declaration.Call.Body;
        var bindings = new Dictionary<ParameterExpression, Expression>();
        bool Match(Expression? shape, Expression? actual)
        {
            if (shape is null || actual is null) return shape is null && actual is null;
            if (shape is ParameterExpression parameter && declaration.Call.Parameters.IndexOf(parameter) is var index && index >= 0)
            {
                bindings.Add(declaration.Replacement.Parameters[index], Visit(actual)!);
                return true;
            }
            return shape.Type == actual.Type && ((shape, actual) switch
            {
                (ConstantExpression first, ConstantExpression second) => Equals(first.Value, second.Value),
                (MemberExpression first, MemberExpression second) => first.Member == second.Member && Match(first.Expression, second.Expression),
                _ => false
            });
        }
        if (!Match(template.Object, node.Object) || !template.Arguments.Zip(node.Arguments).All(pair => Match(pair.First, pair.Second)))
            return base.VisitMethodCall(node);
        if (!expanding.Add(declaration))
            throw new InvalidOperationException("RGD4003: Recursive in-memory translation '" + declaration.Name + "'. Use an acyclic replacement graph.");
        try
        {
            used.Add(declaration);
            return Visit(new InMemoryTranslationParameterReplacer(bindings).Visit(declaration.Replacement.Body)!)!;
        }
        finally { expanding.Remove(declaration); }
    }

    protected override Expression VisitMember(MemberExpression node) => propertyMappings?.Rewrite(node, expression => Visit(expression)!) ?? base.VisitMember(node);

    protected override Expression VisitUnary(UnaryExpression node) => propertyMappings?.Rewrite(node, expression => Visit(expression)!) ?? base.VisitUnary(node);

    protected override Expression VisitIndex(IndexExpression node) => propertyMappings?.Rewrite(node, expression => Visit(expression)!) ?? base.VisitIndex(node);

    private static bool FixedShape(Expression expression) => expression is ConstantExpression constant &&
        (constant.Value is null || Scalar(constant.Type)) || expression is MemberExpression { Expression: null } member &&
        member.Member is PropertyInfo { GetMethod.IsPublic: true } or FieldInfo { IsPublic: true };

    internal static bool Scalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsVisible && (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) ||
            type == typeof(TimeOnly) || type == typeof(TimeSpan));
    }
}
