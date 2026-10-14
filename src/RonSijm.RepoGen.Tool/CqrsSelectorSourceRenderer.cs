using System.Text;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Read selectors use composition, never inheritance from write-capable selector bases.</summary>
internal static class CqrsSelectorSourceRenderer
{
    private const string Cancellation = "global::System.Threading.CancellationToken cancellationToken = default";
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string Ef = "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";

    internal static void AppendEntity(StringBuilder builder, EntityRepositoryModel entity,
        RepositoryGenerationSettingsModel settings, string indentation)
    {
        if (entity.WriteEnabled)
        {
            builder.AppendLine();
            RepositorySourceRenderer.AppendRepositoryInterface(builder, entity, settings, indentation,
                "IWrite" + entity.EntityName + "Repository", includeQueryRoot: false);
        }

        if (!entity.ReadEnabled)
        {
            return;
        }

        AppendReadSelector(builder, entity, settings, indentation, collection: false);
        AppendReadSelector(builder, entity, settings, indentation, collection: true);
        AppendReadRepository(builder, entity, settings, indentation);
    }

    private static void AppendReadSelector(StringBuilder b, EntityRepositoryModel entity,
        RepositoryGenerationSettingsModel settings, string indent, bool collection)
    {
        var source = entity.EntityName + (collection ? "CollectionSelector" : "Selector");
        var name = "Read" + source;
        var type = entity.EntityTypeName;
        var i = indent + "    ";
        b.AppendLine().Append(indent).Append(entity.Accessibility).Append(" sealed class ").Append(name).AppendLine();
        b.Append(indent).AppendLine("{");
        b.Append(i).Append("private readonly ").Append(source).AppendLine(" inner;");
        b.Append(i).Append("internal ").Append(name).Append('(').Append(source).AppendLine(" inner)");
        b.Append(i).AppendLine("{");
        b.Append(i).AppendLine("    this.inner = inner ?? throw new global::System.ArgumentNullException(nameof(inner));");
        b.Append(i).AppendLine("}");

        Method(b, i, name, "Where", $"{Expression(type, "bool")} predicate", "new(inner.Where(predicate))");
        Method(b, i, name, "Include<TNavigation>", $"{Expression(type, "TNavigation")} navigation", "new(inner.Include(navigation))");
        if (settings.GenerateTrackingMethods)
        {
            foreach (var (method, efMethod) in new[]
                     {
                         (settings.NoTrackingMethodName, "AsNoTracking"),
                         (settings.NoTrackingWithIdentityResolutionMethodName, "AsNoTrackingWithIdentityResolution"),
                         (settings.TrackingMethodName, "AsTracking")
                     })
            {
                if (method is not null)
                {
                    Method(b, i, name, method, "", $"new(inner.ConfigureQuery(static query => {Ef}.{efMethod}(query)))");
                }
            }
        }

        Method(b, i, Task + "<bool>", "ExistsAsync", Cancellation, Ef + ".AnyAsync(inner.Query, cancellationToken)");
        if (!collection)
        {
            Method(b, i, Task + "<" + type + "?>", "AsEntityAsync", Cancellation,
                Ef + ".SingleOrDefaultAsync(inner.Query, cancellationToken)");
            if (entity.ProjectionEnabled)
            {
                Method(b, i, Task + "<TProjection?>", "ProjectToAsync<TProjection>",
                    $"{Expression(type, "TProjection")} projection, {Cancellation}", "inner.ProjectToAsync(projection, cancellationToken)");
            }
        }
        else
        {
            foreach (var order in new[] { "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending" })
            {
                Method(b, i, name, order + "<TKey>", $"{Expression(type, "TKey")} keySelector", $"new(inner.{order}(keySelector))");
            }

            if (entity.SortableFields.Count > 0)
            {
                Method(b, i, name, "OrderBy", "string fieldName, bool descending = false", "new(inner.OrderBy(fieldName, descending))");
            }

            foreach (var limit in new[] { "Skip", "Take" })
            {
                Method(b, i, name, limit, "int count", $"new(inner.{limit}(count))");
            }

            Method(b, i, Task + "<int>", "CountAsync", Cancellation, Ef + ".CountAsync(inner.Query, cancellationToken)");
            Method(b, i, Task + "<" + type + "?>", "FirstOrDefaultAsync", Cancellation, "inner.FirstOrDefaultAsync(cancellationToken)");
            b.AppendLine().Append(i).Append("public async ").Append(Task).Append("<global::System.Collections.Generic.IReadOnlyList<")
                .Append(type).Append(">> AsEntitiesAsync(").Append(Cancellation).AppendLine(")");
            b.Append(i).AppendLine("{");
            b.Append(i).Append("    return await ").Append(Ef).AppendLine(".ToListAsync(inner.Query, cancellationToken).ConfigureAwait(false);");
            b.Append(i).AppendLine("}");
            Method(b, i, Task + "<" + settings.InfrastructurePrefix + "PagedResult<" + type + ">>", "ToPagedListAsync",
                "int page, int pageSize, " + Cancellation, "inner.ToPagedListAsync(page, pageSize, cancellationToken)");
            Method(b, i, Task + "<global::System.Collections.Generic.Dictionary<TKey, " + type + ">>", "ToDictionaryAsync<TKey>",
                $"global::System.Func<{type}, TKey> keySelector, {Cancellation}", "inner.ToDictionaryAsync(keySelector, cancellationToken)", "where TKey : notnull");
            AppendEntityStream(b, i, type);
            AppendAggregates(b, i, type);
            if (entity.ProjectionEnabled)
            {
                AppendCollectionProjections(b, i, type, settings);
            }
        }

        foreach (var projection in entity.Projections)
        {
            var extensions = Qualify(projection.Namespace, entity.EntityName + "ConfiguredProjectionExtensions");
            var result = collection ? "global::System.Collections.Generic.IReadOnlyList<" + projection.ProjectionTypeName + ">"
                : projection.ProjectionTypeName.EndsWith('?') ? projection.ProjectionTypeName : projection.ProjectionTypeName + "?";
            Method(b, i, Task + "<" + result + ">", collection ? projection.CollectionMethodName : projection.SingleMethodName,
                Cancellation, extensions + "." + (collection ? projection.CollectionMethodName : projection.SingleMethodName) + "(inner, cancellationToken)",
                accessibility: projection.Accessibility);
        }

        b.Append(indent).AppendLine("}");
    }

    private static void AppendCollectionProjections(StringBuilder b, string i, string type, RepositoryGenerationSettingsModel settings)
    {
        Method(b, i, settings.InfrastructurePrefix + "GroupedCollectionSelector<" + type + ", TKey>", "GroupBy<TKey>",
            $"{Expression(type, "TKey")} keySelector", "inner.GroupBy(keySelector)");
        Method(b, i, Task + "<global::System.Collections.Generic.IReadOnlyList<TProjection>>", "ProjectToListAsync<TProjection>",
            $"{Expression(type, "TProjection")} projection, {Cancellation}", "inner.ProjectToListAsync(projection, cancellationToken)");
        foreach (var terminal in new[] { "ProjectToSingleAsync", "ProjectToSingleOrDefaultAsync", "ProjectToFirstOrDefaultAsync" })
        {
            Method(b, i, Task + "<TProjection" + (terminal == "ProjectToSingleAsync" ? "" : "?") + ">", terminal + "<TProjection>",
                $"{Expression(type, "TProjection")} projection, {Cancellation}", $"inner.{terminal}(projection, cancellationToken)");
        }

        Method(b, i, "global::System.Collections.Generic.IAsyncEnumerable<TProjection>", "StreamAsync<TProjection>",
            $"{Expression(type, "TProjection")} projection, {Cancellation}", "inner.StreamAsync(projection, cancellationToken)");
        Method(b, i, Task + "<" + settings.InfrastructurePrefix + "PagedResult<TProjection>>", "ToPagedListAsync<TProjection>",
            $"{Expression(type, "TProjection")} projection, int page, int pageSize, {Cancellation}", "inner.ToPagedListAsync(projection, page, pageSize, cancellationToken)");
        Method(b, i, Task + "<" + settings.InfrastructurePrefix + "CursorPagedResult<TProjection, TCursor>>", "ToCursorPagedListAsync<TProjection, TCursor>",
            $"{Expression(type, "TProjection")} projection, {Expression(type, "TCursor")} cursorSelector, int pageSize, {Cancellation}",
            "inner.ToCursorPagedListAsync(projection, cursorSelector, pageSize, cancellationToken)");
        Method(b, i, Task + "<global::System.Collections.Generic.Dictionary<TKey, TElement>>", "ToDictionaryAsync<TKey, TElement>",
            $"global::System.Func<{type}, TKey> keySelector, global::System.Func<{type}, TElement> elementSelector, {Cancellation}",
            "inner.ToDictionaryAsync(keySelector, elementSelector, cancellationToken)", "where TKey : notnull");
    }

    private static void AppendEntityStream(StringBuilder b, string i, string type)
    {
        b.AppendLine().Append(i).Append("public async global::System.Collections.Generic.IAsyncEnumerable<").Append(type).AppendLine("> StreamAsync(");
        b.Append(i).Append("    [global::System.Runtime.CompilerServices.EnumeratorCancellation] ").Append(Cancellation).AppendLine(")");
        b.Append(i).AppendLine("{");
        b.Append(i).Append("    await foreach (var item in global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation(")
            .Append(Ef).AppendLine(".AsAsyncEnumerable(inner.Query), cancellationToken).ConfigureAwait(false))");
        b.Append(i).AppendLine("        yield return item;");
        b.Append(i).AppendLine("}");
    }

    private static void AppendAggregates(StringBuilder b, string i, string type)
    {
        foreach (var name in new[] { "MaxAsync", "MinAsync" })
        {
            Method(b, i, Task + "<TResult>", name + "<TResult>", $"{Expression(type, "TResult")} selector, {Cancellation}",
                $"inner.{name}(selector, cancellationToken)");
        }

        foreach (var input in new[] { "Decimal", "Double", "Single", "Int32", "Int64" })
        {
            foreach (var nullable in new[] { "", "?" })
            {
                var number = "global::System." + input + nullable;
                Method(b, i, Task + "<" + number + ">", "SumAsync", $"{Expression(type, number)} selector, {Cancellation}", "inner.SumAsync(selector, cancellationToken)");
                var average = "global::System." + (input is "Int32" or "Int64" ? "Double" : input) + nullable;
                Method(b, i, Task + "<" + average + ">", "AverageAsync", $"{Expression(type, number)} selector, {Cancellation}", "inner.AverageAsync(selector, cancellationToken)");
            }
        }
    }

    private static void AppendReadRepository(StringBuilder b, EntityRepositoryModel entity,
        RepositoryGenerationSettingsModel settings, string indent)
    {
        var name = entity.EntityName;
        var repository = "I" + name + "Repository";
        var port = "IRead" + name + "Repository";
        var i = indent + "    ";
        b.AppendLine().Append(indent).Append(entity.Accessibility).Append(" interface ").Append(port).AppendLine();
        b.Append(indent).AppendLine("{");
        foreach (var (method, parameters, _, unique) in SelectorOperations(entity))
        {
            b.Append(i).Append("Read").Append(name).Append(unique ? "Selector " : "CollectionSelector ")
                .Append(method).Append('(').Append(parameters).AppendLine(");");
        }

        foreach (var query in entity.Queries.Where(query => query.Target == RepositoryQueryTarget.Read && (entity.Accessibility != "public" || query.Accessibility == "public")))
        {
            AppendNamedQuery(b, query, i, implementation: false, entity);
        }

        b.Append(indent).AppendLine("}");

        b.AppendLine().Append(indent).Append("internal sealed class ").Append(name).Append("ReadRepository : ").Append(port).AppendLine();
        b.Append(indent).AppendLine("{");
        b.Append(i).Append("private readonly ").Append(repository).AppendLine(" source;");
        b.Append(i).Append("public ").Append(name).Append("ReadRepository(").Append(repository).AppendLine(" source)");
        b.Append(i).AppendLine("{");
        b.Append(i).AppendLine("    this.source = source ?? throw new global::System.ArgumentNullException(nameof(source));");
        b.Append(i).AppendLine("}");
        foreach (var (method, parameters, arguments, unique) in SelectorOperations(entity))
        {
            Method(b, i, "Read" + name + (unique ? "Selector" : "CollectionSelector"), method, parameters,
                $"Read(this.source.{method}({arguments}))");
        }

        foreach (var query in entity.Queries.Where(static query => query.Target == RepositoryQueryTarget.Read))
        {
            AppendNamedQuery(b, query, i, implementation: true, entity);
        }

        var tracking = settings.ReadOnlyTrackingBehavior == RepositoryTrackingBehavior.Unspecified ? settings.DefaultTrackingBehavior : settings.ReadOnlyTrackingBehavior;
        var efMethod = tracking switch
        {
            RepositoryTrackingBehavior.NoTracking => "AsNoTracking",
            RepositoryTrackingBehavior.NoTrackingWithIdentityResolution => "AsNoTrackingWithIdentityResolution",
            RepositoryTrackingBehavior.Tracking => "AsTracking",
            _ => null
        };
        foreach (var suffix in new[] { "Selector", "CollectionSelector" })
        {
            b.AppendLine().Append(i).Append("private static Read").Append(name).Append(suffix).Append(" Read(").Append(name).Append(suffix).AppendLine(" selector)");
            b.Append(i).AppendLine("{");
            b.Append(i).Append("    return new(").Append(efMethod is null ? "selector" : $"selector.ConfigureQuery(static query => {Ef}.{efMethod}(query))").AppendLine(");");
            b.Append(i).AppendLine("}");
        }

        b.Append(indent).AppendLine("}");
    }

    private static IEnumerable<(string Method, string Parameters, string Arguments, bool Unique)> SelectorOperations(EntityRepositoryModel entity)
    {
        yield return ("GetAll", "", "", false);
        yield return ("GetAllUnordered", "", "", false);
        foreach (var lookup in entity.Lookups)
        {
            yield return (lookup.MethodName, string.Join(", ", lookup.Properties.Select(static p => p.TypeName + " " + p.ParameterName)),
                string.Join(", ", lookup.Properties.Select(static p => p.ParameterName)), lookup.IsUnique);
        }

        foreach (var filter in entity.Filters)
        {
            yield return (filter.MethodName, string.Join(", ", filter.Parameters.Select(static p => p.TypeName + " " + p.Name)),
                string.Join(", ", filter.Parameters.Select(static p => p.Name)), filter.IsUnique);
        }
    }

    internal static void AppendWriteQueries(StringBuilder b, EntityRepositoryModel entity, string indent, bool implementation)
    {
        foreach (var query in entity.Queries.Where(query => query.Target == RepositoryQueryTarget.Write &&
                                                            (entity.Accessibility != "public" || query.Accessibility == "public")))
        {
            AppendNamedQuery(b, query, indent, implementation, entity, receiver: "this");
        }
    }

    private static void AppendNamedQuery(StringBuilder b, ConfiguredQueryModel query, string i, bool implementation, EntityRepositoryModel entity, string receiver = "this.source")
    {
        var parameters = ProjectionSourceRenderer.CreateQueryParameters(query);
        var signature = string.Join(", ", parameters.Select(static p => p.Type + " " + p.Name).Append(Cancellation));
        var result = ProjectionSourceRenderer.RenderQueryReturnType(query);
        if (!implementation)
        {
            b.AppendLine().Append(i).Append(result).Append(' ').Append(query.MethodName).Append('(').Append(signature).AppendLine(");");
        }
        else
        {
            Method(b, i, result, query.MethodName, signature,
                Qualify(query.Namespace, entity.EntityName + "ConfiguredQueryExtensions") + "." + query.MethodName + "(" + receiver + ", " +
                string.Join(", ", parameters.Select(static p => p.Name).Append("cancellationToken")) + ")",
                accessibility: entity.Accessibility != "public" ? "public" : query.Accessibility);
        }
    }

    internal static void AppendRegistration(StringBuilder b, RepositoryGenerationModel model)
    {
        var context = model.ContextTypeName.Split('.').Last();
        const string resolve = "global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService";
        const string register = "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped";
        b.AppendLine().Append("namespace ").Append(model.Settings.InfrastructureNamespace).AppendLine("\n{");
        b.Append("    public static class ").Append(context).AppendLine("SelectorRepositoryRegistration\n    {");
        b.Append("        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add").Append(context)
            .Append("SelectorRepositories(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services")
            .Append(model.Settings.GenerateCacheDecorator ? ", bool useCacheDecorator = false" : "").AppendLine(")\n        {");
        b.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(services);");
        foreach (var entity in model.Entities.Where(static entity => entity.ReadEnabled || entity.WriteEnabled))
        {
            var source = Qualify(entity.Namespace, "I" + entity.EntityName + "Repository");
            var implementation = Qualify(entity.Namespace, entity.EntityName + "Repository");
            b.Append("            ").Append(register).Append('<').Append(source).AppendLine(">(services, provider =>\n            {");
            var contextType = model.Settings.UseDbContextResolver ? model.Settings.InfrastructurePrefix + "IDbContextResolver<" + model.ContextTypeName + ">" : model.ContextTypeName;
            b.Append("                ").Append(source).Append(" repository = new ").Append(implementation).Append('(').Append(resolve).Append('<').Append(contextType).AppendLine(">(provider));");
            if (model.Settings.GenerateCacheDecorator)
            {
                b.Append("                if (useCacheDecorator) repository = new ").Append(Qualify(entity.Namespace, entity.EntityName + "RepositoryCacheDecorator"))
                    .Append("(repository, ").Append(resolve).Append('<').Append(model.Settings.InfrastructurePrefix).AppendLine("IRepositoryCacheProvider>(provider));");
            }

            b.AppendLine("                return repository;\n            });");
            if (entity.ReadEnabled)
            {
                b.Append("            ").Append(register).Append('<').Append(Qualify(entity.Namespace, "IRead" + entity.EntityName + "Repository"))
                    .Append(">(services, static provider => new ").Append(Qualify(entity.Namespace, entity.EntityName + "ReadRepository"))
                    .Append('(').Append(resolve).Append('<').Append(source).AppendLine(">(provider)));");
            }

            if (entity.WriteEnabled)
            {
                b.Append("            ").Append(register).Append('<').Append(Qualify(entity.Namespace, "IWrite" + entity.EntityName + "Repository"))
                    .Append(">(services, static provider => (").Append(Qualify(entity.Namespace, "IWrite" + entity.EntityName + "Repository"))
                    .Append(')').Append(resolve).Append('<').Append(source).AppendLine(">(provider));");
            }
        }

        b.AppendLine("            return services;\n        }\n    }\n}");
    }

    private static string Expression(string type, string result) => $"global::System.Linq.Expressions.Expression<global::System.Func<{type}, {result}>>";
    private static string Qualify(string ns, string type) => "global::" + (ns.Length == 0 ? "" : ns + ".") + type;

    private static void Method(StringBuilder b, string i, string result, string name, string parameters, string call,
        string constraint = "", string accessibility = "public")
    {
        b.AppendLine().Append(i).Append(accessibility).Append(' ').Append(result).Append(' ').Append(name).Append('(').Append(parameters).AppendLine(")");
        if (constraint.Length > 0)
        {
            b.Append(i).Append("    ").AppendLine(constraint);
        }

        b.Append(i).AppendLine("{");
        b.Append(i).Append("    return ").Append(call).AppendLine(";");
        b.Append(i).AppendLine("}");
    }
}