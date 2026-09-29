using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RonSijm.RepoGen.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class RepoGenGenerator : IIncrementalGenerator
{
    private const string GenerateSelectorsAttributeName = "RonSijm.RepoGen.GenerateSelectorsAttribute";
    private const string ProjectionFromAttributeName = "RonSijm.RepoGen.ProjectionFromAttribute`1";
    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GenerateSelectorsAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) => (INamedTypeSymbol)attributeContext.TargetSymbol)
            .Collect();

        var projections = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ProjectionFromAttributeName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attributeContext, _) => CreateProjectionCandidate(attributeContext))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!)
            .Collect();

        var input = entities.Combine(projections).Combine(context.CompilationProvider);

        context.RegisterSourceOutput(
            input,
            static (productionContext, value) => Execute(
                productionContext,
                value.Left.Left,
                value.Left.Right,
                value.Right));
    }

    private static ProjectionCandidate? CreateProjectionCandidate(GeneratorAttributeSyntaxContext context)
    {
        var attribute = context.Attributes.FirstOrDefault();
        if (attribute?.AttributeClass is not INamedTypeSymbol attributeClass ||
            attributeClass.TypeArguments.Length != 1 ||
            attributeClass.TypeArguments[0] is not INamedTypeSymbol entity)
        {
            return null;
        }

        string? methodName = null;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "MethodName" && argument.Value.Value is string configuredName)
            {
                methodName = configuredName;
            }
        }

        return new ProjectionCandidate(
            (INamedTypeSymbol)context.TargetSymbol,
            entity,
            methodName,
            context.TargetNode.GetLocation());
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<INamedTypeSymbol> entityCandidates,
        ImmutableArray<ProjectionCandidate> projectionCandidates,
        Compilation compilation)
    {
        var entities = new Dictionary<INamedTypeSymbol, EntityModel>(SymbolEqualityComparer.Default);
        var seenEntities = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var entity in entityCandidates
                     .OrderBy(static symbol => symbol.ToDisplayString(), StringComparer.Ordinal))
        {
            if (!seenEntities.Add(entity))
            {
                continue;
            }

            if (!IsSupportedEntity(entity))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InvalidSelectorDeclaration,
                    entity.Locations.FirstOrDefault(),
                    entity.ToDisplayString()));
                continue;
            }

            entities.Add(entity, new EntityModel(entity, new List<ProjectionModel>()));
        }

        foreach (var candidate in projectionCandidates
                     .OrderBy(static item => item.Projection.ToDisplayString(), StringComparer.Ordinal))
        {
            if (!entities.TryGetValue(candidate.Entity, out var entityModel))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.InvalidSelectorEntity,
                    candidate.Location,
                    candidate.Projection.ToDisplayString(),
                    candidate.Entity.ToDisplayString()));
                continue;
            }

            var projection = CreateProjectionModel(context, candidate, compilation);
            if (projection is not null)
            {
                entityModel.Projections.Add(projection);
            }
        }

        if (entities.Count > 0)
        {
            context.AddSource(
                "RonSijm.RepoGen.SelectorBases.g.cs",
                SourceText.From(RenderSelectorBases(), Encoding.UTF8));
        }

        foreach (var entity in entities.Values.OrderBy(
                     static item => item.Entity.ToDisplayString(),
                     StringComparer.Ordinal))
        {
            RemoveMethodNameCollisions(context, entity);
            var source = RenderEntity(entity);
            context.AddSource(CreateHintName(entity.Entity), SourceText.From(source, Encoding.UTF8));
        }
    }

    private static bool IsSupportedEntity(INamedTypeSymbol entity) =>
        entity.TypeKind == TypeKind.Class &&
        !entity.IsStatic &&
        entity.ContainingType is null &&
        entity.TypeParameters.Length == 0;

    private static ProjectionModel? CreateProjectionModel(
        SourceProductionContext context,
        ProjectionCandidate candidate,
        Compilation compilation)
    {
        if (candidate.Projection.TypeKind != TypeKind.Class ||
            candidate.Projection.IsStatic ||
            candidate.Projection.ContainingType is not null ||
            candidate.Projection.TypeParameters.Length != 0)
        {
            ReportUnsupported(context, candidate, "only top-level, non-generic class and record projections are supported");
            return null;
        }

        var methodNames = GetMethodNames(candidate);
        if (methodNames is null)
        {
            ReportUnsupported(context, candidate, "the configured method name is not a valid C# identifier");
            return null;
        }

        var customProjection = FindCustomProjection(candidate);
        if (customProjection is not null)
        {
            return new ProjectionModel(
                candidate.Projection,
                methodNames.Value.Single,
                methodNames.Value.Collection,
                customProjection,
                candidate.Location);
        }

        var expression = BuildAutomaticProjection(context, candidate, compilation);
        if (expression is null)
        {
            return null;
        }

        return new ProjectionModel(
            candidate.Projection,
            methodNames.Value.Single,
            methodNames.Value.Collection,
            expression,
            candidate.Location);
    }

    private static (string Single, string Collection)? GetMethodNames(ProjectionCandidate candidate)
    {
        var suffix = candidate.MethodName;
        if (string.IsNullOrWhiteSpace(suffix))
        {
            suffix = candidate.Projection.Name.StartsWith(candidate.Entity.Name, StringComparison.Ordinal) &&
                     candidate.Projection.Name.Length > candidate.Entity.Name.Length
                ? candidate.Projection.Name.Substring(candidate.Entity.Name.Length)
                : candidate.Projection.Name;
        }

        suffix = suffix!.Trim();
        if (suffix.EndsWith("Async", StringComparison.Ordinal))
        {
            suffix = suffix.Substring(0, suffix.Length - "Async".Length);
        }

        if (suffix.StartsWith("As", StringComparison.Ordinal))
        {
            suffix = suffix.Substring(2);
        }

        if (!SyntaxFacts.IsValidIdentifier(suffix))
        {
            return null;
        }

        return ("As" + suffix + "Async", "As" + Pluralize(suffix) + "Async");
    }

    private static string Pluralize(string value)
    {
        if (value.EndsWith("Dto", StringComparison.Ordinal))
        {
            return value + "s";
        }

        if (value.EndsWith("y", StringComparison.Ordinal) && value.Length > 1)
        {
            var previous = char.ToLowerInvariant(value[value.Length - 2]);
            if ("aeiou".IndexOf(previous) < 0)
            {
                return value.Substring(0, value.Length - 1) + "ies";
            }
        }

        if (value.EndsWith("s", StringComparison.Ordinal) ||
            value.EndsWith("x", StringComparison.Ordinal) ||
            value.EndsWith("z", StringComparison.Ordinal) ||
            value.EndsWith("ch", StringComparison.Ordinal) ||
            value.EndsWith("sh", StringComparison.Ordinal))
        {
            return value + "es";
        }

        return value + "s";
    }

    private static string? FindCustomProjection(ProjectionCandidate candidate)
    {
        foreach (var property in candidate.Projection.GetMembers("Projection").OfType<IPropertySymbol>())
        {
            if (!property.IsStatic || property.GetMethod is null || !IsAccessible(property.GetMethod.DeclaredAccessibility))
            {
                continue;
            }

            if (property.Type is not INamedTypeSymbol expression ||
                expression.Name != "Expression" ||
                expression.ContainingNamespace.ToDisplayString() != "System.Linq.Expressions" ||
                expression.TypeArguments.Length != 1 ||
                expression.TypeArguments[0] is not INamedTypeSymbol function ||
                function.Name != "Func" ||
                function.ContainingNamespace.ToDisplayString() != "System" ||
                function.TypeArguments.Length != 2)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(function.TypeArguments[0], candidate.Entity) &&
                SymbolEqualityComparer.Default.Equals(function.TypeArguments[1], candidate.Projection))
            {
                return candidate.Projection.ToDisplayString(FullyQualified) + ".Projection";
            }
        }

        return null;
    }

    private static string? BuildAutomaticProjection(
        SourceProductionContext context,
        ProjectionCandidate candidate,
        Compilation compilation)
    {
        var constructors = candidate.Projection.InstanceConstructors
            .Where(static constructor => IsAccessible(constructor.DeclaredAccessibility))
            .OrderByDescending(static constructor => constructor.Parameters.Length)
            .ThenBy(static constructor => constructor.ToDisplayString(), StringComparer.Ordinal)
            .ToArray();

        var parameterized = constructors.Where(static constructor => constructor.Parameters.Length > 0).ToArray();
        if (parameterized.Length > 0)
        {
            var maximumParameterCount = parameterized[0].Parameters.Length;
            var preferred = parameterized.Where(constructor => constructor.Parameters.Length == maximumParameterCount).ToArray();
            if (preferred.Length != 1)
            {
                ReportUnsupported(context, candidate, "multiple equally specific accessible constructors were found");
                return null;
            }

            var arguments = new List<string>();
            foreach (var parameter in preferred[0].Parameters)
            {
                var targetName = GetProjectionMemberName(candidate.Projection, parameter.Name);
                if (!TryMapMember(context, candidate, targetName, parameter.Type, parameter.Locations.FirstOrDefault(), compilation, out var argument))
                {
                    return null;
                }

                arguments.Add(argument);
            }

            return "static entity => new " + candidate.Projection.ToDisplayString(FullyQualified) +
                   "(" + string.Join(", ", arguments) + ")";
        }

        if (constructors.Length == 0)
        {
            ReportUnsupported(context, candidate, "no accessible constructor or supported static Projection expression was found");
            return null;
        }

        var assignments = new List<string>();
        foreach (var property in candidate.Projection.GetMembers().OfType<IPropertySymbol>()
                     .Where(static property =>
                         !property.IsStatic &&
                         !property.IsIndexer &&
                         property.SetMethod is not null &&
                         IsAccessible(property.SetMethod.DeclaredAccessibility))
                     .OrderBy(static property => property.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue))
        {
            if (!TryMapMember(context, candidate, property.Name, property.Type, property.Locations.FirstOrDefault(), compilation, out var argument))
            {
                return null;
            }

            assignments.Add(EscapeIdentifier(property.Name) + " = " + argument);
        }

        return "static entity => new " + candidate.Projection.ToDisplayString(FullyQualified) +
               " { " + string.Join(", ", assignments) + " }";
    }

    private static string GetProjectionMemberName(INamedTypeSymbol projection, string parameterName)
    {
        var property = projection.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(item => string.Equals(item.Name, parameterName, StringComparison.OrdinalIgnoreCase));

        if (property is not null)
        {
            return property.Name;
        }

        return parameterName.Length == 0
            ? parameterName
            : char.ToUpper(parameterName[0], CultureInfo.InvariantCulture) + parameterName.Substring(1);
    }

    private static bool TryMapMember(
        SourceProductionContext context,
        ProjectionCandidate candidate,
        string memberName,
        ITypeSymbol targetType,
        Location? location,
        Compilation compilation,
        out string expression)
    {
        var entityMember = FindEntityMember(candidate.Entity, memberName);
        if (entityMember is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingMember,
                location ?? candidate.Location,
                candidate.Projection.ToDisplayString(),
                memberName,
                candidate.Entity.ToDisplayString()));
            expression = string.Empty;
            return false;
        }

        var sourceType = GetMemberType(entityMember);
        var conversion = compilation.ClassifyConversion(sourceType, targetType);
        if (!conversion.Exists || !conversion.IsImplicit || HasUnsafeNullabilityConversion(sourceType, targetType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.IncompatibleMember,
                location ?? candidate.Location,
                candidate.Projection.ToDisplayString(),
                memberName,
                targetType.ToDisplayString(),
                candidate.Entity.ToDisplayString(),
                sourceType.ToDisplayString()));
            expression = string.Empty;
            return false;
        }

        expression = "entity." + EscapeIdentifier(entityMember.Name);
        return true;
    }

    private static ISymbol? FindEntityMember(INamedTypeSymbol entity, string memberName)
    {
        for (var current = entity; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(memberName))
            {
                if (member.IsStatic || !IsAccessible(member.DeclaredAccessibility))
                {
                    continue;
                }

                if (member is IPropertySymbol { IsIndexer: false, GetMethod: not null } property &&
                    IsAccessible(property.GetMethod.DeclaredAccessibility))
                {
                    return property;
                }

                if (member is IFieldSymbol)
                {
                    return member;
                }
            }
        }

        return null;
    }

    private static ITypeSymbol GetMemberType(ISymbol member) => member switch
    {
        IPropertySymbol property => property.Type,
        IFieldSymbol field => field.Type,
        _ => throw new InvalidOperationException("Unsupported entity member kind.")
    };

    private static bool HasUnsafeNullabilityConversion(ITypeSymbol source, ITypeSymbol target) =>
        source.IsReferenceType &&
        target.IsReferenceType &&
        source.NullableAnnotation == NullableAnnotation.Annotated &&
        target.NullableAnnotation == NullableAnnotation.NotAnnotated;

    private static bool IsAccessible(Accessibility accessibility) =>
        accessibility == Accessibility.Public ||
        accessibility == Accessibility.Internal ||
        accessibility == Accessibility.ProtectedOrInternal;

    private static void RemoveMethodNameCollisions(SourceProductionContext context, EntityModel entity)
    {
        var reservedSingleName = "AsEntityAsync";
        var reservedCollectionName = "AsEntitiesAsync";
        var collisions = new HashSet<ProjectionModel>();

        foreach (var group in entity.Projections.GroupBy(static projection => projection.SingleMethodName, StringComparer.Ordinal))
        {
            if (group.Count() > 1 || group.Key == reservedSingleName)
            {
                foreach (var projection in group)
                {
                    collisions.Add(projection);
                    context.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.AmbiguousMethodName,
                        projection.Location,
                        entity.Entity.ToDisplayString(),
                        projection.SingleMethodName));
                }
            }
        }

        foreach (var group in entity.Projections.GroupBy(static projection => projection.CollectionMethodName, StringComparer.Ordinal))
        {
            if (group.Count() > 1 || group.Key == reservedCollectionName)
            {
                foreach (var projection in group)
                {
                    if (collisions.Add(projection))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.AmbiguousMethodName,
                            projection.Location,
                            entity.Entity.ToDisplayString(),
                            projection.CollectionMethodName));
                    }
                }
            }
        }

        entity.Projections.RemoveAll(collisions.Contains);
    }

    private static string RenderEntity(EntityModel model)
    {
        var entity = model.Entity;
        var entityType = entity.ToDisplayString(FullyQualified);
        var accessibility = entity.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        var selectorName = entity.Name + "Selector";
        var collectionSelectorName = entity.Name + "CollectionSelector";
        var extensionsName = entity.Name + "SelectorExtensions";
        var collectionExtensionsName = entity.Name + "CollectionSelectorExtensions";
        var builder = new StringBuilder();

        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();

        if (!entity.ContainingNamespace.IsGlobalNamespace)
        {
            builder.Append("namespace ").Append(entity.ContainingNamespace.ToDisplayString()).AppendLine(";");
            builder.AppendLine();
        }

        AppendSelector(builder, accessibility, selectorName, entityType, "BaseEntitySelector");
        builder.AppendLine();
        AppendSelector(builder, accessibility, collectionSelectorName, entityType, "BaseCollectionSelector");
        builder.AppendLine();
        AppendSingleExtensions(builder, accessibility, extensionsName, selectorName, entityType, model.Projections);
        builder.AppendLine();
        AppendCollectionExtensions(builder, accessibility, collectionExtensionsName, collectionSelectorName, entityType, model.Projections);

        return builder.ToString();
    }

    private static void AppendSelector(
        StringBuilder builder,
        string accessibility,
        string selectorName,
        string entityType,
        string baseSelectorName)
    {
        const string cacheInvalidatorArgument = ", CacheInvalidator";
        builder.Append(accessibility).Append(" sealed class ").Append(selectorName)
            .Append(" : global::RonSijm.RepoGen.").Append(baseSelectorName).Append('<').Append(entityType).AppendLine(">");
        builder.AppendLine("{");
        builder.Append("    internal ").Append(selectorName).Append("(global::System.Linq.IQueryable<")
            .Append(entityType).AppendLine("> query)");
        builder.AppendLine("        : base(query)");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    internal ").Append(selectorName).AppendLine("(");
        builder.AppendLine("        global::Microsoft.EntityFrameworkCore.DbContext? context,");
        builder.Append("        global::System.Linq.IQueryable<").Append(entityType).AppendLine("> query)");
        builder.AppendLine("        : base(context, query)");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    internal ").Append(selectorName).AppendLine("(");
        builder.AppendLine("        global::Microsoft.EntityFrameworkCore.DbContext? context,");
        builder.Append("        global::System.Linq.IQueryable<").Append(entityType).AppendLine("> query,");
        builder.AppendLine("        global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>? cacheInvalidator)");
        builder.AppendLine("        : base(context, query, cacheInvalidator)");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        builder.AppendLine();

        builder.Append("    public ").Append(selectorName).AppendLine(" Where(");
        builder.Append("        global::System.Linq.Expressions.Expression<global::System.Func<")
            .Append(entityType).AppendLine(", bool>> predicate)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (predicate is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentNullException(nameof(predicate));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.Append("        return new(Context, global::System.Linq.Queryable.Where(Query, predicate)")
            .Append(cacheInvalidatorArgument).AppendLine(");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    public ").Append(selectorName).AppendLine(" Include<TNavigation>(");
        builder.Append("        global::System.Linq.Expressions.Expression<global::System.Func<")
            .Append(entityType).AppendLine(", TNavigation>> navigation)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (navigation is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentNullException(nameof(navigation));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.Append("        return new(Context, global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(Query, navigation)")
            .Append(cacheInvalidatorArgument).AppendLine(");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        builder.Append("    public ").Append(selectorName).AppendLine(" ConfigureQuery(");
        builder.Append("        global::System.Func<global::System.Linq.IQueryable<").Append(entityType)
            .Append(">, global::System.Linq.IQueryable<").Append(entityType).AppendLine(">> configure)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (configure is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentNullException(nameof(configure));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.Append("        return new(Context, configure(Query)").Append(cacheInvalidatorArgument).AppendLine(");");
        builder.AppendLine("    }");
        builder.AppendLine();
        if (baseSelectorName == "BaseCollectionSelector")
        {
            AppendCollectionQueryMethods(builder, selectorName, entityType);
        }

        builder.Append("    public ").Append(selectorName).AppendLine(" AsNoTracking()");
        builder.AppendLine("    {");
        builder.Append("        return new(Context, global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(Query)")
            .Append(cacheInvalidatorArgument).AppendLine(");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    public ").Append(selectorName).AppendLine(" AsNoTrackingWithIdentityResolution()");
        builder.AppendLine("    {");
        builder.Append("        return new(Context, global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTrackingWithIdentityResolution(Query)")
            .Append(cacheInvalidatorArgument).AppendLine(");");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    public ").Append(selectorName).AppendLine(" AsTracking()");
        builder.AppendLine("    {");
        builder.Append("        return new(Context, global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsTracking(Query)")
            .Append(cacheInvalidatorArgument).AppendLine(");");
        builder.AppendLine("    }");
        builder.AppendLine("}");
    }

    private static void AppendCollectionQueryMethods(
        StringBuilder builder,
        string selectorName,
        string entityType)
    {
        AppendOrderMethod(builder, selectorName, entityType, "OrderBy", requiresOrderedQuery: false);
        AppendOrderMethod(builder, selectorName, entityType, "OrderByDescending", requiresOrderedQuery: false);
        AppendOrderMethod(builder, selectorName, entityType, "ThenBy", requiresOrderedQuery: true);
        AppendOrderMethod(builder, selectorName, entityType, "ThenByDescending", requiresOrderedQuery: true);

        builder.Append("    public ").Append(selectorName).AppendLine(" Skip(int count)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (count < 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(count));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new(Context, global::System.Linq.Queryable.Skip(Query, count), CacheInvalidator);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    public ").Append(selectorName).AppendLine(" Take(int count)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (count < 0)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(count));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new(Context, global::System.Linq.Queryable.Take(Query, count), CacheInvalidator);");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void AppendOrderMethod(
        StringBuilder builder,
        string selectorName,
        string entityType,
        string methodName,
        bool requiresOrderedQuery)
    {
        builder.Append("    public ").Append(selectorName).Append(' ').Append(methodName).AppendLine("<TKey>(");
        builder.Append("        global::System.Linq.Expressions.Expression<global::System.Func<")
            .Append(entityType).AppendLine(", TKey>> keySelector)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (keySelector is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentNullException(nameof(keySelector));");
        builder.AppendLine("        }");
        if (requiresOrderedQuery)
        {
            builder.AppendLine();
            builder.Append("        if (Query is not global::System.Linq.IOrderedQueryable<").Append(entityType)
                .AppendLine("> orderedQuery)");
            builder.AppendLine("        {");
            builder.AppendLine("            throw new global::System.InvalidOperationException(\"ThenBy requires an earlier OrderBy or configured default sort.\");");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.Append("        return new(Context, global::System.Linq.Queryable.").Append(methodName)
                .AppendLine("(orderedQuery, keySelector), CacheInvalidator);");
        }
        else
        {
            builder.Append("        return new(Context, global::System.Linq.Queryable.").Append(methodName)
                .AppendLine("(Query, keySelector), CacheInvalidator);");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static string RenderSelectorBases()
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("namespace RonSijm.RepoGen;");
        builder.AppendLine();
        AppendPagedResult(builder, string.Empty);
        builder.AppendLine();
        AppendCursorPagedResult(builder, string.Empty);
        builder.AppendLine();
        AppendGroupedCollectionSelector(builder, string.Empty);
        builder.AppendLine();
        builder.AppendLine("public abstract class BaseEntitySelector<TEntity>");
        builder.AppendLine("    where TEntity : class");
        builder.AppendLine("{");
        builder.AppendLine("    private protected global::Microsoft.EntityFrameworkCore.DbContext? Context { get; }");
        builder.AppendLine();
        builder.AppendLine("    private protected global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>? CacheInvalidator { get; }");
        builder.AppendLine();
        builder.AppendLine("    internal global::System.Linq.IQueryable<TEntity> Query { get; }");
        builder.AppendLine();
        builder.AppendLine("    internal global::Microsoft.EntityFrameworkCore.DbContext? SelectorContext => Context;");
        builder.AppendLine();
        builder.AppendLine("    protected BaseEntitySelector(global::System.Linq.IQueryable<TEntity> query)");
        builder.AppendLine("        : this(null, query, null)");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    protected BaseEntitySelector(");
        builder.AppendLine("        global::Microsoft.EntityFrameworkCore.DbContext? context,");
        builder.AppendLine("        global::System.Linq.IQueryable<TEntity> query)");
        builder.AppendLine("        : this(context, query, null)");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    protected BaseEntitySelector(");
        builder.AppendLine("        global::Microsoft.EntityFrameworkCore.DbContext? context,");
        builder.AppendLine("        global::System.Linq.IQueryable<TEntity> query,");
        builder.AppendLine("        global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>? cacheInvalidator)");
        builder.AppendLine("    {");
        builder.AppendLine("        Context = context;");
        builder.AppendLine("        Query = query ?? throw new global::System.ArgumentNullException(nameof(query));");
        builder.AppendLine("        CacheInvalidator = cacheInvalidator;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public global::System.Threading.Tasks.Task<TProjection?> ProjectToAsync<TProjection>(");
        builder.AppendLine("        global::System.Linq.Expressions.Expression<global::System.Func<TEntity, TProjection>> projection,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (projection is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentNullException(nameof(projection));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(");
        builder.AppendLine("            global::System.Linq.Queryable.Select(Query, projection),");
        builder.AppendLine("            cancellationToken);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public DeleteOperation Delete()");
        builder.AppendLine("    {");
        builder.AppendLine("        return new(RequireContext(), Query, CacheInvalidator);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public DeleteOperation Remove()");
        builder.AppendLine("    {");
        builder.AppendLine("        return Delete();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public UpdateOperation Update(global::System.Action<TEntity> update)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (update is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentNullException(nameof(update));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new(RequireContext(), Query, update, CacheInvalidator);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public async global::System.Threading.Tasks.Task<TEntity?> StageUpdateAsync(");
        builder.AppendLine("        global::System.Action<TEntity> update,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (update is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentNullException(nameof(update));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        var context = RequireContext();");
        builder.AppendLine("        var entity = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(");
        builder.AppendLine("                Query,");
        builder.AppendLine("                cancellationToken)");
        builder.AppendLine("            .ConfigureAwait(false);");
        builder.AppendLine("        if (entity is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        update(entity);");
        builder.AppendLine("        context.Update(entity);");
        builder.AppendLine("        return entity;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public global::System.Threading.Tasks.Task<TEntity?> UpdateAsync(");
        builder.AppendLine("        global::System.Action<TEntity> update,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        return Update(update).SaveChangesAsync(cancellationToken);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public async global::System.Threading.Tasks.Task<bool> MarkForDeletionAsync(");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        var context = RequireContext();");
        builder.AppendLine("        var entity = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(");
        builder.AppendLine("                Query,");
        builder.AppendLine("                cancellationToken)");
        builder.AppendLine("            .ConfigureAwait(false);");
        builder.AppendLine("        if (entity is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        context.Remove(entity);");
        builder.AppendLine("        return true;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public global::System.Threading.Tasks.Task<bool> DeleteAsync(");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        return Delete().SaveChangesAsync(cancellationToken);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public global::System.Threading.Tasks.Task<bool> RemoveAsync(");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        return DeleteAsync(cancellationToken);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public sealed class DeleteOperation");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::Microsoft.EntityFrameworkCore.DbContext context;");
        builder.AppendLine("        private readonly global::System.Linq.IQueryable<TEntity> query;");
        builder.AppendLine("        private readonly global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>? cacheInvalidator;");
        builder.AppendLine();
        builder.AppendLine("        internal DeleteOperation(");
        builder.AppendLine("            global::Microsoft.EntityFrameworkCore.DbContext context,");
        builder.AppendLine("            global::System.Linq.IQueryable<TEntity> query,");
        builder.AppendLine("            global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>? cacheInvalidator)");
        builder.AppendLine("        {");
        builder.AppendLine("            this.context = context;");
        builder.AppendLine("            this.query = query;");
        builder.AppendLine("            this.cacheInvalidator = cacheInvalidator;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public bool SaveChanges()");
        builder.AppendLine("        {");
        builder.AppendLine("            var entity = global::System.Linq.Queryable.SingleOrDefault(query);");
        builder.AppendLine("            if (entity is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            context.Remove(entity);");
        builder.AppendLine("            context.SaveChanges();");
        builder.AppendLine("            cacheInvalidator?.Invoke(global::System.Threading.CancellationToken.None).GetAwaiter().GetResult();");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public async global::System.Threading.Tasks.Task<bool> SaveChangesAsync(");
        builder.AppendLine("            global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            var entity = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(");
        builder.AppendLine("                    query,");
        builder.AppendLine("                    cancellationToken)");
        builder.AppendLine("                .ConfigureAwait(false);");
        builder.AppendLine("            if (entity is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            context.Remove(entity);");
        builder.AppendLine("            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            if (cacheInvalidator is not null)");
        builder.AppendLine("            {");
        builder.AppendLine("                await cacheInvalidator(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            }");
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public sealed class UpdateOperation");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::Microsoft.EntityFrameworkCore.DbContext context;");
        builder.AppendLine("        private readonly global::System.Linq.IQueryable<TEntity> query;");
        builder.AppendLine("        private readonly global::System.Action<TEntity> update;");
        builder.AppendLine("        private readonly global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>? cacheInvalidator;");
        builder.AppendLine();
        builder.AppendLine("        internal UpdateOperation(");
        builder.AppendLine("            global::Microsoft.EntityFrameworkCore.DbContext context,");
        builder.AppendLine("            global::System.Linq.IQueryable<TEntity> query,");
        builder.AppendLine("            global::System.Action<TEntity> update,");
        builder.AppendLine("            global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>? cacheInvalidator)");
        builder.AppendLine("        {");
        builder.AppendLine("            this.context = context;");
        builder.AppendLine("            this.query = query;");
        builder.AppendLine("            this.update = update;");
        builder.AppendLine("            this.cacheInvalidator = cacheInvalidator;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public TEntity? SaveChanges()");
        builder.AppendLine("        {");
        builder.AppendLine("            var entity = global::System.Linq.Queryable.SingleOrDefault(query);");
        builder.AppendLine("            if (entity is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            update(entity);");
        builder.AppendLine("            context.Update(entity);");
        builder.AppendLine("            context.SaveChanges();");
        builder.AppendLine("            cacheInvalidator?.Invoke(global::System.Threading.CancellationToken.None).GetAwaiter().GetResult();");
        builder.AppendLine("            return entity;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public async global::System.Threading.Tasks.Task<TEntity?> SaveChangesAsync(");
        builder.AppendLine("            global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("        {");
        builder.AppendLine("            var entity = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(");
        builder.AppendLine("                    query,");
        builder.AppendLine("                    cancellationToken)");
        builder.AppendLine("                .ConfigureAwait(false);");
        builder.AppendLine("            if (entity is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            update(entity);");
        builder.AppendLine("            context.Update(entity);");
        builder.AppendLine("            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            if (cacheInvalidator is not null)");
        builder.AppendLine("            {");
        builder.AppendLine("                await cacheInvalidator(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("            }");
        builder.AppendLine("            return entity;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private global::Microsoft.EntityFrameworkCore.DbContext RequireContext()");
        builder.AppendLine("    {");
        builder.AppendLine("        return Context ?? throw new global::System.InvalidOperationException(");
        builder.AppendLine("            \"This selector was created without a DbContext and cannot perform write operations.\");");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("public abstract class BaseCollectionSelector<TEntity>");
        builder.AppendLine("    where TEntity : class");
        builder.AppendLine("{");
        builder.AppendLine("    private protected global::Microsoft.EntityFrameworkCore.DbContext? Context { get; }");
        builder.AppendLine();
        builder.AppendLine("    private protected global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>? CacheInvalidator { get; }");
        builder.AppendLine();
        builder.AppendLine("    internal global::System.Linq.IQueryable<TEntity> Query { get; }");
        builder.AppendLine();
        builder.AppendLine("    internal global::Microsoft.EntityFrameworkCore.DbContext? SelectorContext => Context;");
        builder.AppendLine();
        builder.AppendLine("    internal global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>? SelectorCacheInvalidator => CacheInvalidator;");
        builder.AppendLine();
        builder.AppendLine("    protected BaseCollectionSelector(global::System.Linq.IQueryable<TEntity> query)");
        builder.AppendLine("        : this(null, query, null)");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    protected BaseCollectionSelector(");
        builder.AppendLine("        global::Microsoft.EntityFrameworkCore.DbContext? context,");
        builder.AppendLine("        global::System.Linq.IQueryable<TEntity> query)");
        builder.AppendLine("        : this(context, query, null)");
        builder.AppendLine("    {");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    protected BaseCollectionSelector(");
        builder.AppendLine("        global::Microsoft.EntityFrameworkCore.DbContext? context,");
        builder.AppendLine("        global::System.Linq.IQueryable<TEntity> query,");
        builder.AppendLine("        global::System.Func<global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task>? cacheInvalidator)");
        builder.AppendLine("    {");
        builder.AppendLine("        Context = context;");
        builder.AppendLine("        Query = query ?? throw new global::System.ArgumentNullException(nameof(query));");
        builder.AppendLine("        CacheInvalidator = cacheInvalidator;");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public GroupedCollectionSelector<TEntity, TKey> GroupBy<TKey>(");
        builder.AppendLine("        global::System.Linq.Expressions.Expression<global::System.Func<TEntity, TKey>> keySelector)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (keySelector is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentNullException(nameof(keySelector));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return new(global::System.Linq.Queryable.GroupBy(Query, keySelector));");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<TProjection>> ProjectToListAsync<TProjection>(");
        builder.AppendLine("        global::System.Linq.Expressions.Expression<global::System.Func<TEntity, TProjection>> projection,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (projection is null)");
        builder.AppendLine("        {");
        builder.AppendLine("            throw new global::System.ArgumentNullException(nameof(projection));");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        return await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
        builder.AppendLine("                global::System.Linq.Queryable.Select(Query, projection),");
        builder.AppendLine("                cancellationToken)");
        builder.AppendLine("            .ConfigureAwait(false);");
        builder.AppendLine("    }");
        builder.AppendLine();
        AppendCollectionTerminalMethods(builder, "    ", includeSetBasedOperations: false);
        AppendConfiguredQueryTerminalMethods(builder, "    ");
        AppendPagedListMethods(builder, "    ");
        AppendCursorPagedListMethod(builder, "    ");
        AppendAggregateMethods(builder, "    ");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendPagedResult(StringBuilder builder, string indentation)
    {
        builder.Append(indentation).AppendLine("public sealed class PagedResult<T>");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    public PagedResult(");
        builder.Append(indentation).AppendLine("        global::System.Collections.Generic.IReadOnlyList<T> items,");
        builder.Append(indentation).AppendLine("        int totalCount,");
        builder.Append(indentation).AppendLine("        int page,");
        builder.Append(indentation).AppendLine("        int pageSize)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        Items = items ?? throw new global::System.ArgumentNullException(nameof(items));");
        builder.Append(indentation).AppendLine("        TotalCount = totalCount;");
        builder.Append(indentation).AppendLine("        Page = page;");
        builder.Append(indentation).AppendLine("        PageSize = pageSize;");
        builder.Append(indentation).AppendLine("    }");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("    public global::System.Collections.Generic.IReadOnlyList<T> Items { get; }");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("    public int TotalCount { get; }");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("    public int Page { get; }");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("    public int PageSize { get; }");
        builder.Append(indentation).AppendLine("}");
    }

    private static void AppendCursorPagedResult(StringBuilder builder, string indentation)
    {
        builder.Append(indentation).AppendLine("public readonly struct CursorPageRequest<TCursor>");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    private CursorPageRequest(bool hasCursor, TCursor? cursor, int pageSize)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        if (pageSize < 1) throw new global::System.ArgumentOutOfRangeException(nameof(pageSize), \"Page size must be at least 1.\");");
        builder.Append(indentation).AppendLine("        HasCursor = hasCursor;");
        builder.Append(indentation).AppendLine("        Cursor = cursor;");
        builder.Append(indentation).AppendLine("        PageSize = pageSize;");
        builder.Append(indentation).AppendLine("    }");
        builder.Append(indentation).AppendLine("    public bool HasCursor { get; }");
        builder.Append(indentation).AppendLine("    public TCursor? Cursor { get; }");
        builder.Append(indentation).AppendLine("    public int PageSize { get; }");
        builder.Append(indentation).AppendLine("    public static CursorPageRequest<TCursor> First(int pageSize) => new(false, default, pageSize);");
        builder.Append(indentation).AppendLine("    public static CursorPageRequest<TCursor> After(TCursor cursor, int pageSize) => new(true, cursor, pageSize);");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("public sealed class CursorPagedResult<T, TCursor>");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    public CursorPagedResult(global::System.Collections.Generic.IReadOnlyList<T> items, bool hasMore, TCursor? nextCursor)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        Items = items ?? throw new global::System.ArgumentNullException(nameof(items));");
        builder.Append(indentation).AppendLine("        HasMore = hasMore;");
        builder.Append(indentation).AppendLine("        NextCursor = nextCursor;");
        builder.Append(indentation).AppendLine("    }");
        builder.Append(indentation).AppendLine("    public global::System.Collections.Generic.IReadOnlyList<T> Items { get; }");
        builder.Append(indentation).AppendLine("    public bool HasMore { get; }");
        builder.Append(indentation).AppendLine("    public TCursor? NextCursor { get; }");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("internal sealed class CursorPageEntry<T, TCursor>");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    public T Item { get; set; } = default!;");
        builder.Append(indentation).AppendLine("    public TCursor Cursor { get; set; } = default!;");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("internal sealed class ParameterReplaceVisitor(global::System.Linq.Expressions.ParameterExpression parameter, global::System.Linq.Expressions.Expression replacement)");
        builder.Append(indentation).AppendLine("    : global::System.Linq.Expressions.ExpressionVisitor");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    protected override global::System.Linq.Expressions.Expression VisitParameter(global::System.Linq.Expressions.ParameterExpression node)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        return node == parameter ? replacement : base.VisitParameter(node);");
        builder.Append(indentation).AppendLine("    }");
        builder.Append(indentation).AppendLine("}");
    }

    private static void AppendCollectionTerminalMethods(
        StringBuilder builder,
        string indentation,
        bool includeSetBasedOperations)
    {
        builder.Append(indentation).AppendLine("public global::System.Threading.Tasks.Task<TEntity?> FirstOrDefaultAsync(");
        builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(Query, cancellationToken);");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("public global::System.Threading.Tasks.Task<global::System.Collections.Generic.Dictionary<TKey, TEntity>> ToDictionaryAsync<TKey>(");
        builder.Append(indentation).AppendLine("    global::System.Func<TEntity, TKey> keySelector,");
        builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("    where TKey : notnull");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    if (keySelector is null) throw new global::System.ArgumentNullException(nameof(keySelector));");
        builder.Append(indentation).AppendLine("    return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(Query, keySelector, cancellationToken);");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("public global::System.Threading.Tasks.Task<global::System.Collections.Generic.Dictionary<TKey, TElement>> ToDictionaryAsync<TKey, TElement>(");
        builder.Append(indentation).AppendLine("    global::System.Func<TEntity, TKey> keySelector,");
        builder.Append(indentation).AppendLine("    global::System.Func<TEntity, TElement> elementSelector,");
        builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("    where TKey : notnull");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    if (keySelector is null) throw new global::System.ArgumentNullException(nameof(keySelector));");
        builder.Append(indentation).AppendLine("    if (elementSelector is null) throw new global::System.ArgumentNullException(nameof(elementSelector));");
        builder.Append(indentation).AppendLine("    return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(Query, keySelector, elementSelector, cancellationToken);");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();

        if (!includeSetBasedOperations)
        {
            return;
        }

        builder.Append(indentation).AppendLine("public async global::System.Threading.Tasks.Task<int> ExecuteDeleteAsync(");
        builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    var affected = await global::Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteDeleteAsync(Query, cancellationToken).ConfigureAwait(false);");
        builder.Append(indentation).AppendLine("    if (affected > 0 && CacheInvalidator is not null)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        await CacheInvalidator(cancellationToken).ConfigureAwait(false);");
        builder.Append(indentation).AppendLine("    }");
        builder.Append(indentation).AppendLine("    return affected;");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("public async global::System.Threading.Tasks.Task<int> ExecuteUpdateAsync(");
        builder.Append(indentation).AppendLine("    global::System.Linq.Expressions.Expression<global::System.Func<global::Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<TEntity>, global::Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<TEntity>>> setters,");
        builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    if (setters is null) throw new global::System.ArgumentNullException(nameof(setters));");
        builder.Append(indentation).AppendLine("    var affected = await global::Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteUpdateAsync(Query, setters, cancellationToken).ConfigureAwait(false);");
        builder.Append(indentation).AppendLine("    if (affected > 0 && CacheInvalidator is not null)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        await CacheInvalidator(cancellationToken).ConfigureAwait(false);");
        builder.Append(indentation).AppendLine("    }");
        builder.Append(indentation).AppendLine("    return affected;");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendConfiguredQueryTerminalMethods(StringBuilder builder, string indentation)
    {
        foreach (var terminal in new[]
                 {
                     (Name: "ProjectToSingleAsync", EfName: "SingleAsync", Nullable: false),
                     (Name: "ProjectToSingleOrDefaultAsync", EfName: "SingleOrDefaultAsync", Nullable: true),
                     (Name: "ProjectToFirstOrDefaultAsync", EfName: "FirstOrDefaultAsync", Nullable: true)
                 })
        {
            builder.Append(indentation).Append("public global::System.Threading.Tasks.Task<TProjection")
                .Append(terminal.Nullable ? "?" : string.Empty).Append("> ").Append(terminal.Name).AppendLine("<TProjection>(");
            builder.Append(indentation).AppendLine("    global::System.Linq.Expressions.Expression<global::System.Func<TEntity, TProjection>> projection,");
            builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
            builder.Append(indentation).AppendLine("{");
            builder.Append(indentation).AppendLine("    if (projection is null) throw new global::System.ArgumentNullException(nameof(projection));");
            builder.Append(indentation).Append("    return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.")
                .Append(terminal.EfName).AppendLine("(global::System.Linq.Queryable.Select(Query, projection), cancellationToken);");
            builder.Append(indentation).AppendLine("}");
            builder.AppendLine();
        }

        builder.Append(indentation).AppendLine("public async global::System.Collections.Generic.IAsyncEnumerable<TProjection> StreamAsync<TProjection>(");
        builder.Append(indentation).AppendLine("    global::System.Linq.Expressions.Expression<global::System.Func<TEntity, TProjection>> projection,");
        builder.Append(indentation).AppendLine("    [global::System.Runtime.CompilerServices.EnumeratorCancellation] global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    if (projection is null) throw new global::System.ArgumentNullException(nameof(projection));");
        builder.Append(indentation).AppendLine("    await foreach (var item in global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation(global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsAsyncEnumerable(global::System.Linq.Queryable.Select(Query, projection)), cancellationToken).ConfigureAwait(false))");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        yield return item;");
        builder.Append(indentation).AppendLine("    }");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendCursorPagedListMethod(StringBuilder builder, string indentation)
    {
        builder.Append(indentation).AppendLine("public async global::System.Threading.Tasks.Task<CursorPagedResult<TProjection, TCursor>> ToCursorPagedListAsync<TProjection, TCursor>(");
        builder.Append(indentation).AppendLine("    global::System.Linq.Expressions.Expression<global::System.Func<TEntity, TProjection>> projection,");
        builder.Append(indentation).AppendLine("    global::System.Linq.Expressions.Expression<global::System.Func<TEntity, TCursor>> cursorSelector,");
        builder.Append(indentation).AppendLine("    int pageSize,");
        builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    if (projection is null) throw new global::System.ArgumentNullException(nameof(projection));");
        builder.Append(indentation).AppendLine("    if (cursorSelector is null) throw new global::System.ArgumentNullException(nameof(cursorSelector));");
        builder.Append(indentation).AppendLine("    if (pageSize < 1) throw new global::System.ArgumentOutOfRangeException(nameof(pageSize), \"Page size must be at least 1.\");");
        builder.Append(indentation).AppendLine("    var parameter = global::System.Linq.Expressions.Expression.Parameter(typeof(TEntity), \"entity\");");
        builder.Append(indentation).AppendLine("    var item = new ParameterReplaceVisitor(projection.Parameters[0], parameter).Visit(projection.Body)!;");
        builder.Append(indentation).AppendLine("    var cursor = new ParameterReplaceVisitor(cursorSelector.Parameters[0], parameter).Visit(cursorSelector.Body)!;");
        builder.Append(indentation).AppendLine("    var entryType = typeof(CursorPageEntry<TProjection, TCursor>);");
        builder.Append(indentation).AppendLine("    var initializer = global::System.Linq.Expressions.Expression.MemberInit(global::System.Linq.Expressions.Expression.New(entryType), global::System.Linq.Expressions.Expression.Bind(entryType.GetProperty(nameof(CursorPageEntry<TProjection, TCursor>.Item))!, item), global::System.Linq.Expressions.Expression.Bind(entryType.GetProperty(nameof(CursorPageEntry<TProjection, TCursor>.Cursor))!, cursor));");
        builder.Append(indentation).AppendLine("    var entrySelector = global::System.Linq.Expressions.Expression.Lambda<global::System.Func<TEntity, CursorPageEntry<TProjection, TCursor>>>(initializer, parameter);");
        builder.Append(indentation).AppendLine("    var entries = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(global::System.Linq.Queryable.Select(global::System.Linq.Queryable.Take(Query, checked(pageSize + 1)), entrySelector), cancellationToken).ConfigureAwait(false);");
        builder.Append(indentation).AppendLine("    var hasMore = entries.Count > pageSize;");
        builder.Append(indentation).AppendLine("    if (hasMore) entries.RemoveAt(entries.Count - 1);");
        builder.Append(indentation).AppendLine("    var items = entries.ConvertAll(static entry => entry.Item);");
        builder.Append(indentation).AppendLine("    var nextCursor = hasMore && entries.Count > 0 ? entries[entries.Count - 1].Cursor : default;");
        builder.Append(indentation).AppendLine("    return new CursorPagedResult<TProjection, TCursor>(items, hasMore, nextCursor);");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendGroupedCollectionSelector(StringBuilder builder, string indentation)
    {
        builder.Append(indentation).AppendLine("public sealed class GroupedCollectionSelector<TEntity, TKey>");
        builder.Append(indentation).AppendLine("    where TEntity : class");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    private readonly global::System.Linq.IQueryable<global::System.Linq.IGrouping<TKey, TEntity>> query;");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("    internal GroupedCollectionSelector(");
        builder.Append(indentation).AppendLine("        global::System.Linq.IQueryable<global::System.Linq.IGrouping<TKey, TEntity>> query)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        this.query = query ?? throw new global::System.ArgumentNullException(nameof(query));");
        builder.Append(indentation).AppendLine("    }");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("    public async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<TResult>> ProjectToListAsync<TResult>(");
        builder.Append(indentation).AppendLine("        global::System.Linq.Expressions.Expression<global::System.Func<global::System.Linq.IGrouping<TKey, TEntity>, TResult>> projection,");
        builder.Append(indentation).AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        if (projection is null)");
        builder.Append(indentation).AppendLine("        {");
        builder.Append(indentation).AppendLine("            throw new global::System.ArgumentNullException(nameof(projection));");
        builder.Append(indentation).AppendLine("        }");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("        return await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
        builder.Append(indentation).AppendLine("                global::System.Linq.Queryable.Select(query, projection),");
        builder.Append(indentation).AppendLine("                cancellationToken)");
        builder.Append(indentation).AppendLine("            .ConfigureAwait(false);");
        builder.Append(indentation).AppendLine("    }");
        builder.Append(indentation).AppendLine("}");
    }

    private static void AppendPagedListMethods(StringBuilder builder, string indentation)
    {
        builder.Append(indentation).AppendLine("public global::System.Threading.Tasks.Task<PagedResult<TEntity>> ToPagedListAsync(");
        builder.Append(indentation).AppendLine("    int page,");
        builder.Append(indentation).AppendLine("    int pageSize,");
        builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    return ToPagedListAsync<TEntity>(static entity => entity, page, pageSize, cancellationToken);");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("public async global::System.Threading.Tasks.Task<PagedResult<TProjection>> ToPagedListAsync<TProjection>(");
        builder.Append(indentation).AppendLine("    global::System.Linq.Expressions.Expression<global::System.Func<TEntity, TProjection>> projection,");
        builder.Append(indentation).AppendLine("    int page,");
        builder.Append(indentation).AppendLine("    int pageSize,");
        builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    if (projection is null)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        throw new global::System.ArgumentNullException(nameof(projection));");
        builder.Append(indentation).AppendLine("    }");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("    if (page < 1)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        throw new global::System.ArgumentOutOfRangeException(nameof(page), \"Page must be at least 1.\");");
        builder.Append(indentation).AppendLine("    }");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("    if (pageSize < 1)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        throw new global::System.ArgumentOutOfRangeException(nameof(pageSize), \"Page size must be at least 1.\");");
        builder.Append(indentation).AppendLine("    }");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("    var offset = checked((page - 1) * pageSize);");
        builder.Append(indentation).AppendLine("    var totalCount = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(");
        builder.Append(indentation).AppendLine("            Query,");
        builder.Append(indentation).AppendLine("            cancellationToken)");
        builder.Append(indentation).AppendLine("        .ConfigureAwait(false);");
        builder.Append(indentation).AppendLine("    var pageQuery = global::System.Linq.Queryable.Take(");
        builder.Append(indentation).AppendLine("        global::System.Linq.Queryable.Skip(Query, offset),");
        builder.Append(indentation).AppendLine("        pageSize);");
        builder.Append(indentation).AppendLine("    var items = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
        builder.Append(indentation).AppendLine("            global::System.Linq.Queryable.Select(pageQuery, projection),");
        builder.Append(indentation).AppendLine("            cancellationToken)");
        builder.Append(indentation).AppendLine("        .ConfigureAwait(false);");
        builder.AppendLine();
        builder.Append(indentation).AppendLine("    return new PagedResult<TProjection>(items, totalCount, page, pageSize);");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendAggregateMethods(StringBuilder builder, string indentation)
    {
        AppendGenericAggregateMethod(builder, indentation, "MaxAsync");
        AppendGenericAggregateMethod(builder, indentation, "MinAsync");
        AppendNumericAggregateMethod(builder, indentation, "SumAsync", "global::System.Decimal", "global::System.Decimal");
        AppendNumericAggregateMethod(builder, indentation, "SumAsync", "global::System.Decimal?", "global::System.Decimal?");
        AppendNumericAggregateMethod(builder, indentation, "SumAsync", "global::System.Double", "global::System.Double");
        AppendNumericAggregateMethod(builder, indentation, "SumAsync", "global::System.Double?", "global::System.Double?");
        AppendNumericAggregateMethod(builder, indentation, "SumAsync", "global::System.Single", "global::System.Single");
        AppendNumericAggregateMethod(builder, indentation, "SumAsync", "global::System.Single?", "global::System.Single?");
        AppendNumericAggregateMethod(builder, indentation, "SumAsync", "global::System.Int32", "global::System.Int32");
        AppendNumericAggregateMethod(builder, indentation, "SumAsync", "global::System.Int32?", "global::System.Int32?");
        AppendNumericAggregateMethod(builder, indentation, "SumAsync", "global::System.Int64", "global::System.Int64");
        AppendNumericAggregateMethod(builder, indentation, "SumAsync", "global::System.Int64?", "global::System.Int64?");
        AppendNumericAggregateMethod(builder, indentation, "AverageAsync", "global::System.Decimal", "global::System.Decimal");
        AppendNumericAggregateMethod(builder, indentation, "AverageAsync", "global::System.Decimal?", "global::System.Decimal?");
        AppendNumericAggregateMethod(builder, indentation, "AverageAsync", "global::System.Double", "global::System.Double");
        AppendNumericAggregateMethod(builder, indentation, "AverageAsync", "global::System.Double?", "global::System.Double?");
        AppendNumericAggregateMethod(builder, indentation, "AverageAsync", "global::System.Single", "global::System.Single");
        AppendNumericAggregateMethod(builder, indentation, "AverageAsync", "global::System.Single?", "global::System.Single?");
        AppendNumericAggregateMethod(builder, indentation, "AverageAsync", "global::System.Int32", "global::System.Double");
        AppendNumericAggregateMethod(builder, indentation, "AverageAsync", "global::System.Int32?", "global::System.Double?");
        AppendNumericAggregateMethod(builder, indentation, "AverageAsync", "global::System.Int64", "global::System.Double");
        AppendNumericAggregateMethod(builder, indentation, "AverageAsync", "global::System.Int64?", "global::System.Double?");
    }

    private static void AppendGenericAggregateMethod(StringBuilder builder, string indentation, string methodName)
    {
        builder.Append(indentation).Append("public global::System.Threading.Tasks.Task<TResult> ").Append(methodName)
            .AppendLine("<TResult>(");
        builder.Append(indentation).AppendLine("    global::System.Linq.Expressions.Expression<global::System.Func<TEntity, TResult>> selector,");
        builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    if (selector is null)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        throw new global::System.ArgumentNullException(nameof(selector));");
        builder.Append(indentation).AppendLine("    }");
        builder.AppendLine();
        builder.Append(indentation).Append("    return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.")
            .Append(methodName).AppendLine("(Query, selector, cancellationToken);");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendNumericAggregateMethod(
        StringBuilder builder,
        string indentation,
        string methodName,
        string inputType,
        string resultType)
    {
        builder.Append(indentation).Append("public global::System.Threading.Tasks.Task<").Append(resultType).Append("> ")
            .Append(methodName).AppendLine("(");
        builder.Append(indentation).Append("    global::System.Linq.Expressions.Expression<global::System.Func<TEntity, ")
            .Append(inputType).AppendLine(">> selector,");
        builder.Append(indentation).AppendLine("    global::System.Threading.CancellationToken cancellationToken = default)");
        builder.Append(indentation).AppendLine("{");
        builder.Append(indentation).AppendLine("    if (selector is null)");
        builder.Append(indentation).AppendLine("    {");
        builder.Append(indentation).AppendLine("        throw new global::System.ArgumentNullException(nameof(selector));");
        builder.Append(indentation).AppendLine("    }");
        builder.AppendLine();
        builder.Append(indentation).Append("    return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.")
            .Append(methodName).AppendLine("(Query, selector, cancellationToken);");
        builder.Append(indentation).AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendSingleExtensions(
        StringBuilder builder,
        string accessibility,
        string extensionsName,
        string selectorName,
        string entityType,
        IEnumerable<ProjectionModel> projections)
    {
        builder.Append(accessibility).Append(" static class ").Append(extensionsName).AppendLine();
        builder.AppendLine("{");
        builder.Append("    public static global::System.Threading.Tasks.Task<").Append(entityType).AppendLine("?> AsEntityAsync(");
        builder.Append("        this ").Append(selectorName).AppendLine(" selector,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(");
        builder.AppendLine("            selector.Query,");
        builder.AppendLine("            cancellationToken);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    public static global::System.Threading.Tasks.Task<bool> ExistsAsync(").AppendLine();
        builder.Append("        this ").Append(selectorName).AppendLine(" selector,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(");
        builder.AppendLine("            selector.Query,");
        builder.AppendLine("            cancellationToken);");
        builder.AppendLine("    }");

        foreach (var projection in projections.OrderBy(static item => item.SingleMethodName, StringComparer.Ordinal))
        {
            var methodAccessibility = projection.Projection.DeclaredAccessibility == Accessibility.Public
                ? "public"
                : "internal";
            builder.AppendLine();
            builder.Append("    ").Append(methodAccessibility).Append(" static global::System.Threading.Tasks.Task<")
                .Append(projection.Projection.ToDisplayString(FullyQualified)).Append("?> ")
                .Append(projection.SingleMethodName).AppendLine("(");
            builder.Append("        this ").Append(selectorName).AppendLine(" selector,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleOrDefaultAsync(");
            builder.AppendLine("            global::System.Linq.Queryable.Select(");
            builder.AppendLine("                selector.Query,");
            builder.Append("                ").Append(projection.Expression).AppendLine("),");
            builder.AppendLine("            cancellationToken);");
            builder.AppendLine("    }");
        }

        builder.AppendLine("}");
    }

    private static void AppendCollectionExtensions(
        StringBuilder builder,
        string accessibility,
        string extensionsName,
        string selectorName,
        string entityType,
        IEnumerable<ProjectionModel> projections)
    {
        builder.Append(accessibility).Append(" static class ").Append(extensionsName).AppendLine();
        builder.AppendLine("{");
        builder.Append("    public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<")
            .Append(entityType).AppendLine(">> AsEntitiesAsync(");
        builder.Append("        this ").Append(selectorName).AppendLine(" selector,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        return await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
        builder.AppendLine("                selector.Query,");
        builder.AppendLine("                cancellationToken)");
        builder.AppendLine("            .ConfigureAwait(false);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::System.Threading.Tasks.Task<bool> ExistsAsync(");
        builder.Append("        this ").Append(selectorName).AppendLine(" selector,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(");
        builder.AppendLine("            selector.Query,");
        builder.AppendLine("            cancellationToken);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public static global::System.Threading.Tasks.Task<int> CountAsync(");
        builder.Append("        this ").Append(selectorName).AppendLine(" selector,");
        builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
        builder.AppendLine("    {");
        builder.AppendLine("        return global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(");
        builder.AppendLine("            selector.Query,");
        builder.AppendLine("            cancellationToken);");
        builder.AppendLine("    }");

        foreach (var projection in projections.OrderBy(static item => item.CollectionMethodName, StringComparer.Ordinal))
        {
            var projectionType = projection.Projection.ToDisplayString(FullyQualified);
            var methodAccessibility = projection.Projection.DeclaredAccessibility == Accessibility.Public
                ? "public"
                : "internal";
            builder.AppendLine();
            builder.Append("    ").Append(methodAccessibility)
                .Append(" static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<")
                .Append(projectionType).Append(">> ").Append(projection.CollectionMethodName).AppendLine("(");
            builder.Append("        this ").Append(selectorName).AppendLine(" selector,");
            builder.AppendLine("        global::System.Threading.CancellationToken cancellationToken = default)");
            builder.AppendLine("    {");
            builder.AppendLine("        return await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(");
            builder.AppendLine("                global::System.Linq.Queryable.Select(");
            builder.AppendLine("                    selector.Query,");
            builder.Append("                    ").Append(projection.Expression).AppendLine("),");
            builder.AppendLine("                cancellationToken)");
            builder.AppendLine("            .ConfigureAwait(false);");
            builder.AppendLine("    }");
        }

        builder.AppendLine("}");
    }

    private static void ReportUnsupported(
        SourceProductionContext context,
        ProjectionCandidate candidate,
        string reason) =>
        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.UnsupportedProjection,
            candidate.Location,
            candidate.Projection.ToDisplayString(),
            reason));

    private static string EscapeIdentifier(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : "@" + identifier;

    private static string CreateHintName(INamedTypeSymbol entity)
    {
        var fullName = entity.ToDisplayString();
        var builder = new StringBuilder(fullName.Length + 20);
        foreach (var character in fullName)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.Append(".Selectors.g.cs").ToString();
    }
}
