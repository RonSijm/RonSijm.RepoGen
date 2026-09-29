using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using RonSijm.RepoGen.Design;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RepositoryDesignModel = RonSijm.RepoGen.Design.RepositoryDesign;

namespace RonSijm.RepoGen.Tool;

internal static class RepositoryModelFactory
{
    public static RepositoryGenerationModel Create(
        DbContext context,
        Assembly targetAssembly,
        bool generateSelectors = false,
        RepositoryDesignModel? design = null)
    {
        var contextTypeName = CSharpTypeName(context.GetType());
        var configuredOptions = design?.Options ?? new RepositoryGenerationOptions();
        var settings = new RepositoryGenerationSettingsModel(
            configuredOptions.UseDbContextResolver,
            configuredOptions.GenerateCacheDecorator,
            configuredOptions.GenerateBatchOperations,
            configuredOptions.GenerateSetBasedOperations,
            configuredOptions.GenerateConcurrencyRetry,
            configuredOptions.GenerateTransactions,
            configuredOptions.GenerateSynchronousMethods,
            configuredOptions.DefaultTrackingBehavior);
        var entities = context.Model.GetEntityTypes()
            .Where(static entity =>
                !entity.IsOwned() &&
                entity.ClrType is { IsGenericType: false, IsNested: false })
            .OrderBy(static entity => entity.ClrType.FullName, StringComparer.Ordinal)
            .Select(entity => CreateEntity(entity, contextTypeName, targetAssembly, generateSelectors, design, settings))
            .ToArray();

        if (entities.Length == 0)
        {
            throw new InvalidOperationException(
                $"{context.GetType().FullName} does not contain any supported entities.");
        }

        var finalizedEntities = entities.Select(entity =>
        {
            var invalidationTags = new HashSet<string>(entity.CacheInvalidationTags, StringComparer.Ordinal)
            {
                entity.CacheTag
            };
            foreach (var owner in entities)
            {
                if (owner.Queries.Any(query =>
                        query.Cache.Enabled &&
                        query.Cache.DependentEntityTypeNames.Contains(entity.EntityTypeName, StringComparer.Ordinal)))
                {
                    invalidationTags.Add(owner.CacheTag);
                }
            }

            return entity with
            {
                CacheInvalidationTags = invalidationTags.OrderBy(static tag => tag, StringComparer.Ordinal).ToArray()
            };
        }).ToArray();

        return new RepositoryGenerationModel(contextTypeName, finalizedEntities, settings);
    }

    private static EntityRepositoryModel CreateEntity(
        IEntityType entity,
        string contextTypeName,
        Assembly targetAssembly,
        bool generateSelectors,
        RepositoryDesignModel? design,
        RepositoryGenerationSettingsModel settings)
    {
        var entityType = entity.ClrType;
        var entityDesign = design?.Entities.FirstOrDefault(item => item.EntityType == entityType);
        var excludedProperties = entityDesign?.ExcludedProperties is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(entityDesign.ExcludedProperties, StringComparer.Ordinal);
        var explicitProperties = entityDesign?.Accessors
            .Select(static accessor => accessor.PropertyName)
            .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        var lookups = new List<LookupModel>();
        var propertySets = new HashSet<string>(StringComparer.Ordinal);

        AddLookup(
            entity.FindPrimaryKey()?.Properties,
            isUnique: true,
            methodName: null,
            lookups,
            propertySets,
            excludedProperties,
            explicitProperties);
        foreach (var index in entity.GetIndexes()
                     .OrderBy(static index => string.Join("|", index.Properties.Select(static property => property.Name)), StringComparer.Ordinal))
        {
            AddLookup(
                index.Properties,
                index.IsUnique,
                methodName: null,
                lookups,
                propertySets,
                excludedProperties,
                explicitProperties);
        }

        if (entityDesign is not null)
        {
            foreach (var accessor in entityDesign.Accessors)
            {
                var property = entity.FindProperty(accessor.PropertyName)
                               ?? throw new InvalidOperationException(
                                   $"Configured accessor '{entityType.FullName}.{accessor.PropertyName}' is not part of the EF model.");
                if (!string.IsNullOrWhiteSpace(accessor.MethodName))
                {
                    var inferredLookup = lookups.FirstOrDefault(lookup =>
                        lookup.Properties.Count == 1 &&
                        string.Equals(lookup.Properties[0].PropertyName, property.Name, StringComparison.Ordinal));
                    if (inferredLookup is not null)
                    {
                        lookups.Remove(inferredLookup);
                        propertySets.Remove(property.Name);
                    }
                }

                AddLookup(
                    [property],
                    accessor.IsUnique,
                    accessor.MethodName,
                    lookups,
                    propertySets,
                    excludedProperties,
                    explicitProperties: null);
            }
        }

        var selectorFullName = string.IsNullOrEmpty(entityType.Namespace)
            ? entityType.Name + "Selector"
            : entityType.Namespace + "." + entityType.Name + "Selector";
        var filters = CreateFilters(entityDesign);
        ValidateMethodSignatures(entityType, lookups, filters);

        return new EntityRepositoryModel(
            entityType.Namespace ?? string.Empty,
            entityType.IsPublic ? "public" : "internal",
            entityType.Name,
            CSharpTypeName(entityType),
            contextTypeName,
            generateSelectors ||
            targetAssembly.GetType(selectorFullName, throwOnError: false, ignoreCase: false) is null,
            lookups,
            filters,
            CreateDefaultSorts(entityDesign),
            CreateSortableFields(entity, entityDesign),
            CreateIncludes(entityDesign),
            CreateProjections(entity, entityDesign),
            CreateConfiguredQueries(entity, entityDesign, settings),
            CreateConfiguredCommands(entityDesign),
            entityType.FullName ?? entityType.Name,
            [entityType.FullName ?? entityType.Name]);
    }

    private static FilterModel[] CreateFilters(RepositoryEntityDesign? design)
    {
        if (design is null)
        {
            return [];
        }

        return design.Filters.Select(filter =>
        {
            var parameterNames = filter.Predicate.Parameters
                .Skip(1)
                .Select((parameter, index) => ToParameterName(parameter.Name ?? "value" + (index + 1)))
                .ToArray();
            var expressionNames = new string[parameterNames.Length + 1];
            expressionNames[0] = "entity";
            parameterNames.CopyTo(expressionNames, 1);
            var parameters = filter.Predicate.Parameters
                .Skip(1)
                .Select((parameter, index) => new FilterParameterModel(
                    parameterNames[index],
                    CSharpTypeName(parameter.Type)))
                .ToArray();

            return new FilterModel(
                filter.MethodName,
                filter.IsUnique,
                parameters,
                ProjectionExpressionRenderer.RenderBody(filter.Predicate, expressionNames));
        }).ToArray();
    }

    private static SortModel[] CreateDefaultSorts(RepositoryEntityDesign? design) =>
        design?.DefaultSorts
            .Select(sort => new SortModel(
                sort.IsDescending,
                ProjectionExpressionRenderer.Render(sort.KeySelector)))
            .ToArray() ?? [];

    private static SortableFieldModel[] CreateSortableFields(
        IEntityType entity,
        RepositoryEntityDesign? design)
    {
        if (design is null)
        {
            return [];
        }

        var configuredFields = design.SortableFields
            .Select(static field => field.PropertyName)
            .Concat(design.Queries
                .Where(static query => query.DynamicSort is not null)
                .SelectMany(static query => query.DynamicSort!.PropertyNames))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var duplicate = configuredFields
            .GroupBy(static field => field, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Entity '{entity.ClrType.FullName}' configures sortable fields that differ only by casing: '{duplicate.Key}'.");
        }

        return configuredFields.Select(propertyName =>
        {
            _ = entity.FindProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"Configured sortable field '{entity.ClrType.FullName}.{propertyName}' is not part of the EF model.");
            return new SortableFieldModel(propertyName);
        }).ToArray();
    }

    private static IncludeModel[] CreateIncludes(RepositoryEntityDesign? design) =>
        design?.Includes
            .Select(include => new IncludeModel(ProjectionExpressionRenderer.Render(include.Navigation)))
            .ToArray() ?? [];

    private static void ValidateMethodSignatures(
        Type entityType,
        IReadOnlyList<LookupModel> lookups,
        IReadOnlyList<FilterModel> filters)
    {
        var duplicate = lookups
            .Select(lookup => lookup.MethodName + "(" +
                              string.Join(",", lookup.Properties.Select(static property => property.TypeName)) + ")")
            .Concat(filters.Select(filter => filter.MethodName + "(" +
                                             string.Join(",", filter.Parameters.Select(static parameter => parameter.TypeName)) + ")"))
            .GroupBy(static signature => signature, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Repository '{entityType.FullName}' configures duplicate method signature '{duplicate.Key}'.");
        }
    }

    private static void AddLookup(
        IReadOnlyList<IProperty>? properties,
        bool isUnique,
        string? methodName,
        List<LookupModel> lookups,
        HashSet<string> propertySets,
        HashSet<string> excludedProperties,
        HashSet<string>? explicitProperties)
    {
        if (properties is null ||
            properties.Count == 0 ||
            properties.Any(static property => property.IsShadowProperty()) ||
            properties.Any(property => excludedProperties.Contains(property.Name)) ||
            explicitProperties is not null &&
            properties.Count == 1 &&
            explicitProperties.Contains(properties[0].Name))
        {
            return;
        }

        var propertySet = string.Join("|", properties.Select(static property => property.Name));
        if (!propertySets.Add(propertySet))
        {
            return;
        }

        var lookupProperties = properties.Select(property => new LookupPropertyModel(
            property.Name,
            ToParameterName(property.Name),
            CSharpTypeName(property.ClrType, property.IsNullable))).ToArray();
        methodName ??= "By" + string.Join("And", properties.Select(static property => property.Name));
        lookups.Add(new LookupModel(methodName, isUnique, lookupProperties));
    }

    private static ProjectionModel[] CreateProjections(IEntityType entity, RepositoryEntityDesign? design)
    {
        if (design is null)
        {
            return [];
        }

        return design.Projections.Select(projection =>
        {
            var suffix = projection.MethodName;
            if (string.IsNullOrWhiteSpace(suffix))
            {
                suffix = projection.ProjectionType.Name;
            }

            if (suffix!.EndsWith("Async", StringComparison.Ordinal))
            {
                suffix = suffix[..^"Async".Length];
            }

            if (suffix.StartsWith("As", StringComparison.Ordinal))
            {
                suffix = suffix[2..];
            }

            return new ProjectionModel(
                projection.ProjectionType.Namespace ?? string.Empty,
                projection.ProjectionType.IsPublic ? "public" : "internal",
                CSharpTypeName(projection.ProjectionType),
                "As" + suffix + "Async",
                "As" + Pluralize(suffix) + "Async",
                ProjectionExpressionRenderer.Render(projection.Expression),
                CreateProjectionIncludes(entity, projection));
        }).ToArray();
    }

    private static IncludeModel[] CreateProjectionIncludes(
        IEntityType entity,
        RepositoryProjectionDesign projection)
    {
        var includes = new List<IncludeModel>();
        var expressionSources = new HashSet<string>(StringComparer.Ordinal);

        foreach (var include in projection.Includes)
        {
            AddProjectionInclude(include.Navigation, includes, expressionSources);
        }

        foreach (var navigationName in ProjectionNavigationFinder.Find(entity, projection.Expression))
        {
            var parameter = projection.Expression.Parameters[0];
            var navigation = Expression.Lambda(
                Expression.Property(parameter, navigationName),
                parameter);
            AddProjectionInclude(navigation, includes, expressionSources);
        }

        return includes.ToArray();
    }

    private static void AddProjectionInclude(
        LambdaExpression navigation,
        List<IncludeModel> includes,
        HashSet<string> expressionSources)
    {
        var expressionSource = ProjectionExpressionRenderer.Render(navigation);
        if (expressionSources.Add(expressionSource))
        {
            includes.Add(new IncludeModel(expressionSource));
        }
    }

    private sealed class ProjectionNavigationFinder : ExpressionVisitor
    {
        private readonly IEntityType entity;
        private readonly ParameterExpression parameter;
        private readonly HashSet<string> navigationNames = new(StringComparer.Ordinal);

        private ProjectionNavigationFinder(IEntityType entity, ParameterExpression parameter)
        {
            this.entity = entity;
            this.parameter = parameter;
        }

        public static string[] Find(IEntityType entity, LambdaExpression projection)
        {
            var finder = new ProjectionNavigationFinder(entity, projection.Parameters[0]);
            finder.Visit(projection.Body);
            return finder.navigationNames.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (TryGetRootMember(node, out var memberName) &&
                (entity.FindNavigation(memberName) is not null ||
                 entity.FindSkipNavigation(memberName) is not null))
            {
                navigationNames.Add(memberName);
            }

            return base.VisitMember(node);
        }

        private bool TryGetRootMember(MemberExpression member, out string memberName)
        {
            var current = member;
            while (current.Expression is MemberExpression parent)
            {
                current = parent;
            }

            memberName = current.Member.Name;
            return current.Expression == parameter;
        }
    }

    private static ConfiguredQueryModel[] CreateConfiguredQueries(
        IEntityType entity,
        RepositoryEntityDesign? design,
        RepositoryGenerationSettingsModel settings)
    {
        if (design is null)
        {
            return [];
        }

        return design.Queries.Select(query =>
        {
            var cacheEnabled = settings.GenerateCacheDecorator && query.Cache.Enabled;
            if (cacheEnabled && query.ResultKind == RepositoryQueryResultKind.Stream)
            {
                throw new InvalidOperationException(
                    $"Configured query '{entity.ClrType.FullName}.{query.MethodName}' cannot cache a streamed result.");
            }

            if (query.ResultKind == RepositoryQueryResultKind.CursorPaged && query.Cursor is null)
            {
                throw new InvalidOperationException(
                    $"Configured query '{entity.ClrType.FullName}.{query.MethodName}' requires cursor configuration.");
            }

            if (query.ResultKind == RepositoryQueryResultKind.GroupedList && query.Group is null)
            {
                throw new InvalidOperationException(
                    $"Configured query '{entity.ClrType.FullName}.{query.MethodName}' requires group configuration.");
            }

            var predicates = CreateConfiguredPredicates(query.Predicates, out var parameters);
            var dynamicSort = CreateConfiguredDynamicSort(entity, query.DynamicSort);
            if (dynamicSort is not null)
            {
                AddParameter(parameters, dynamicSort.FieldNameParameter, CSharpTypeName(typeof(string)));
                AddParameter(parameters, dynamicSort.DescendingParameter, CSharpTypeName(typeof(bool)));
            }

            var projection = query.Projection;
            var resultType = query.Group?.ResultType ?? projection?.ProjectionType ?? entity.ClrType;
            var projectionExpression = projection is null
                ? "static entity => entity"
                : ProjectionExpressionRenderer.Render(projection.Expression);
            var projectionBody = projection is null
                ? "entity"
                : ProjectionExpressionRenderer.RenderBody(projection.Expression, ["entity"]);
            var includes = CreateConfiguredQueryIncludes(entity, query);
            var dependencies = new HashSet<string>(
                query.Cache.DependentEntityTypes.Select(static type => CSharpTypeName(type)),
                StringComparer.Ordinal);
            if (projection is not null)
            {
                foreach (var navigationName in ProjectionNavigationFinder.Find(entity, projection.Expression))
                {
                    var target = entity.FindNavigation(navigationName)?.TargetEntityType ??
                                 entity.FindSkipNavigation(navigationName)?.TargetEntityType;
                    if (target is not null)
                    {
                        dependencies.Add(CSharpTypeName(target.ClrType));
                    }
                }
            }

            var cursor = query.Cursor is null
                ? null
                : new ConfiguredCursorModel(
                    CSharpTypeName(query.Cursor.CursorType),
                    ProjectionExpressionRenderer.RenderBody(query.Cursor.SeekPredicate, ["entity", "cursor"]),
                    ProjectionExpressionRenderer.Render(query.Cursor.CursorSelector),
                    ProjectionExpressionRenderer.RenderBody(query.Cursor.CursorSelector, ["entity"]));
            var group = query.Group is null
                ? null
                : new ConfiguredGroupedQueryModel(
                    ProjectionExpressionRenderer.Render(query.Group.KeySelector),
                    ProjectionExpressionRenderer.Render(query.Group.Projection));

            return new ConfiguredQueryModel(
                resultType.Namespace ?? entity.ClrType.Namespace ?? string.Empty,
                entity.ClrType.IsPublic && resultType.IsPublic ? "public" : "internal",
                EnsureAsyncSuffix(query.MethodName),
                CSharpTypeName(resultType),
                query.ResultKind,
                predicates,
                parameters,
                includes,
                query.Sorts.Select(sort => new SortModel(
                    sort.IsDescending,
                    ProjectionExpressionRenderer.Render(sort.KeySelector))).ToArray(),
                dynamicSort,
                projectionExpression,
                projectionBody,
                query.TrackingBehavior == RepositoryTrackingBehavior.Unspecified
                    ? settings.DefaultTrackingBehavior
                    : query.TrackingBehavior,
                query.SplittingBehavior,
                query.IgnoreQueryFilters,
                query.QueryTag,
                new ConfiguredQueryCacheModel(
                    cacheEnabled,
                    query.Cache.AbsoluteExpirationRelativeToNow?.Ticks,
                    query.Cache.SlidingExpiration?.Ticks,
                    dependencies.OrderBy(static name => name, StringComparer.Ordinal).ToArray()),
                cursor,
                group);
        }).ToArray();
    }

    private static ConfiguredCommandModel[] CreateConfiguredCommands(RepositoryEntityDesign? design)
    {
        if (design is null)
        {
            return [];
        }

        return design.Commands.Select(command =>
        {
            if (command.Kind == RepositoryCommandKind.Unspecified)
            {
                throw new InvalidOperationException(
                    $"Configured command '{command.MethodName}' must end in Delete() or Update(...).");
            }

            if (command.Kind == RepositoryCommandKind.Update && command.Updates.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Configured update command '{command.MethodName}' must set at least one property.");
            }

            var predicates = CreateConfiguredPredicates(command.Predicates, out var parameters);
            var updates = command.Updates.Select(update =>
            {
                var parameterNames = update.Value.Parameters
                    .Skip(1)
                    .Select((parameter, index) => ToParameterName(parameter.Name ?? "value" + (index + 1)))
                    .ToArray();
                var expressionNames = new string[parameterNames.Length + 1];
                expressionNames[0] = "entity";
                parameterNames.CopyTo(expressionNames, 1);
                foreach (var parameter in update.Value.Parameters.Skip(1).Select((value, index) => new
                {
                    Name = parameterNames[index],
                    TypeName = CSharpTypeName(value.Type)
                }))
                {
                    AddParameter(parameters, parameter.Name, parameter.TypeName);
                }

                return new ConfiguredUpdateModel(
                    ProjectionExpressionRenderer.Render(update.Property),
                    "entity => " + ProjectionExpressionRenderer.RenderBody(update.Value, expressionNames));
            }).ToArray();
            return new ConfiguredCommandModel(
                EnsureAsyncSuffix(command.MethodName),
                command.Kind,
                predicates,
                parameters,
                updates);
        }).ToArray();
    }

    private static ConfiguredPredicateModel[] CreateConfiguredPredicates(
        IEnumerable<LambdaExpression> predicates,
        out List<FilterParameterModel> parameters)
    {
        parameters = [];
        var result = new List<ConfiguredPredicateModel>();
        foreach (var predicate in predicates)
        {
            var parameterNames = predicate.Parameters
                .Skip(1)
                .Select((parameter, index) => ToParameterName(parameter.Name ?? "value" + (index + 1)))
                .ToArray();
            var expressionNames = new string[parameterNames.Length + 1];
            expressionNames[0] = "entity";
            parameterNames.CopyTo(expressionNames, 1);
            foreach (var parameter in predicate.Parameters.Skip(1).Select((value, index) => new
            {
                Name = parameterNames[index],
                TypeName = CSharpTypeName(value.Type)
            }))
            {
                AddParameter(parameters, parameter.Name, parameter.TypeName);
            }

            result.Add(new ConfiguredPredicateModel(
                ProjectionExpressionRenderer.RenderBody(predicate, expressionNames)));
        }

        return result.ToArray();
    }

    private static void AddParameter(
        List<FilterParameterModel> parameters,
        string name,
        string typeName)
    {
        var existing = parameters.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, name, StringComparison.Ordinal));
        if (existing is null)
        {
            parameters.Add(new FilterParameterModel(name, typeName));
            return;
        }

        if (!string.Equals(existing.TypeName, typeName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configured parameter '{name}' is used with both '{existing.TypeName}' and '{typeName}'.");
        }
    }

    private static ConfiguredDynamicSortModel? CreateConfiguredDynamicSort(
        IEntityType entity,
        RepositoryDynamicSortDesign? dynamicSort)
    {
        if (dynamicSort is null)
        {
            return null;
        }

        var fields = dynamicSort.PropertyNames.Select(propertyName =>
        {
            _ = entity.FindProperty(propertyName)
                ?? throw new InvalidOperationException(
                    $"Configured sortable field '{entity.ClrType.FullName}.{propertyName}' is not part of the EF model.");
            return new SortableFieldModel(propertyName);
        }).ToArray();
        return new ConfiguredDynamicSortModel(
            ToParameterName(dynamicSort.FieldNameParameter),
            ToParameterName(dynamicSort.DescendingParameter),
            fields);
    }

    private static IncludeModel[] CreateConfiguredQueryIncludes(
        IEntityType entity,
        RepositoryQueryDesign query)
    {
        var includes = new List<IncludeModel>();
        var expressionSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var include in query.Includes)
        {
            AddProjectionInclude(include.Navigation, includes, expressionSources);
        }

        if (query.Projection is not null)
        {
            foreach (var include in CreateProjectionIncludes(entity, query.Projection))
            {
                if (expressionSources.Add(include.ExpressionSource))
                {
                    includes.Add(include);
                }
            }
        }

        return includes.ToArray();
    }

    private static string EnsureAsyncSuffix(string methodName) =>
        methodName.EndsWith("Async", StringComparison.Ordinal) ? methodName : methodName + "Async";

    private static string Pluralize(string value)
    {
        if (value.EndsWith("Dto", StringComparison.Ordinal))
        {
            return value + "s";
        }

        if (value.EndsWith('y') && value.Length > 1 &&
            "aeiou".IndexOf(char.ToLowerInvariant(value[value.Length - 2])) < 0)
        {
            return value[..^1] + "ies";
        }

        if (value.EndsWith('s') ||
            value.EndsWith('x') ||
            value.EndsWith('z') ||
            value.EndsWith("ch", StringComparison.Ordinal) ||
            value.EndsWith("sh", StringComparison.Ordinal))
        {
            return value + "es";
        }

        return value + "s";
    }

    private static string ToParameterName(string propertyName)
    {
        if (propertyName.Length == 0)
        {
            return "value";
        }

        var result = char.ToLower(propertyName[0], CultureInfo.InvariantCulture) + propertyName[1..];
        return CSharpKeywords.Contains(result) ? "@" + result : result;
    }

    internal static string CSharpTypeName(Type type, bool nullable = false)
    {
        if (type.IsArray)
        {
            return CSharpTypeName(type.GetElementType()!) + "[]" + (nullable ? "?" : string.Empty);
        }

        var nullableUnderlyingType = Nullable.GetUnderlyingType(type);
        if (nullableUnderlyingType is not null)
        {
            return CSharpTypeName(nullableUnderlyingType) + "?";
        }

        string result;
        if (type.IsGenericType)
        {
            var definitionName = type.GetGenericTypeDefinition().FullName
                                 ?? throw new InvalidOperationException($"Type '{type}' does not have a full name.");
            definitionName = definitionName[..definitionName.IndexOf('`')].Replace('+', '.');
            result = "global::" + definitionName + "<" +
                     string.Join(", ", type.GetGenericArguments().Select(static argument => CSharpTypeName(argument))) + ">";
        }
        else
        {
            result = "global::" + (type.FullName ?? type.Name).Replace('+', '.');
        }

        return nullable && !type.IsValueType ? result + "?" : result;
    }

    internal static string EscapeIdentifier(string value) =>
        CSharpKeywords.Contains(value) ? "@" + value : value;

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while"
    };
}