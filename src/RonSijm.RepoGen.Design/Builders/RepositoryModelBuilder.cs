using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryModelBuilder
{
    private readonly Dictionary<Type, RepositoryEntityDesign> entities = new();
    private readonly RepositoryGenerationOptions options = new();
    private readonly List<RepositoryTypeAdapterDesign> typeAdapters = new();
    private readonly HashSet<(Type RowType, Type? RequestType, string Name)> fragmentNames = new();

    /// <summary>Creates a reusable row predicate without registering the row as an entity. Supports DTOs and scalars.</summary>
    public RepositoryQueryFragment<TRow> HasFragment<TRow>(string name, Expression<Func<TRow, bool>> predicate)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        return new RepositoryQueryFragment<TRow>(RegisterFragment(typeof(TRow), null, name), predicate);
    }

    /// <summary>Creates a reusable request-bound row predicate without registering the row as an entity.</summary>
    public RepositoryQueryFragment<TRow, TRequest> HasFragment<TRow, TRequest>(string name, Expression<Func<TRow, TRequest, bool>> predicate)
    {
        if (predicate is null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        return new RepositoryQueryFragment<TRow, TRequest>(RegisterFragment(typeof(TRow), typeof(TRequest), name), predicate);
    }

    private string RegisterFragment(Type rowType, Type? requestType, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A fragment name is required.", nameof(name));
        }

        var trimmed = name.Trim();
        if (!fragmentNames.Add((rowType, requestType, trimmed)))
        {
            throw new InvalidOperationException($"RGD2201: Model fragment '{trimmed}' is already registered for row '{rowType.FullName}' and request '{requestType?.FullName ?? "none"}'. Use distinct names across modules.");
        }

        return trimmed;
    }

    /// <summary>Registers an explicit reusable ID/model mapping. Does not automatically map unrelated properties of the same CLR type.</summary>
    public RepositoryTypeAdapter<TModel, TContract> HasTypeAdapter<TModel, TContract>(
        Expression<Func<TModel, TContract>> toContract, Expression<Func<TContract, TModel>> toModel)
    {
        if (toContract is null)
        {
            throw new ArgumentNullException(nameof(toContract));
        }

        if (toModel is null)
        {
            throw new ArgumentNullException(nameof(toModel));
        }

        var adapter = new RepositoryTypeAdapter<TModel, TContract>(toContract, toModel);
        typeAdapters.Add(adapter.Design);
        return adapter;
    }

    /// <summary>Applies an explicitly registered module; modules run in registration order.</summary>
    public RepositoryModelBuilder ApplyConfiguration(IRepositoryModelConfiguration configuration)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        configuration.Configure(this);
        return this;
    }

    public RepositoryModelBuilder ApplyConfiguration<TEntity>(IRepositoryEntityConfiguration<TEntity> configuration)
        where TEntity : class
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        configuration.Configure(Entity<TEntity>());
        return this;
    }

    public RepositoryModelBuilder Configure(Action<RepositoryGenerationOptionsBuilder> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(new RepositoryGenerationOptionsBuilder(options));
        return this;
    }

    public RepositoryEntityBuilder<TEntity> Entity<TEntity>() where TEntity : class
    {
        var entityType = typeof(TEntity);
        if (!entities.TryGetValue(entityType, out var entity))
        {
            entity = new RepositoryEntityDesign(entityType);
            entities.Add(entityType, entity);
        }

        return new RepositoryEntityBuilder<TEntity>(entity);
    }

    public RepositoryModelBuilder Entity<TEntity>(Action<RepositoryEntityBuilder<TEntity>> configure) where TEntity : class
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        configure(Entity<TEntity>());
        return this;
    }

    public RepositoryDesign Build()
    {
        var adapterExpressions = new RepositoryAdapterExpressions(typeAdapters);

        foreach (var entity in entities.Values.Where(entity => entity.RepositoryEnabled))
        {
            adapterExpressions.Normalize(entity, (options.ContractNamespace is null && !options.CqrsSelectors) || entity.ReadEnabled,
                (options.ContractNamespace is null && !options.CqrsSelectors) || entity.WriteEnabled);
            var duplicateProfile = entity.LoadProfiles.GroupBy(profile => profile.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
            if (duplicateProfile is not null)
            {
                throw new InvalidOperationException($"RGD1003: Entity '{entity.EntityType.FullName}' declares duplicate load profile '{duplicateProfile.Key}'. Use distinct profile names across modules.");
            }

            entity.Loading.Resolve(entity, entity.EntityType.FullName ?? entity.EntityType.Name);
            foreach (var projection in entity.Projections)
            {
                projection.Loading.Resolve(entity, projection.MethodName ?? projection.ProjectionType.Name);
            }

            var queries = entity.Queries.Where(query => (options.ContractNamespace is null && !options.CqrsSelectors) ||
                                                        (query.Target == RepositoryQueryTarget.Write ? entity.WriteEnabled : entity.ReadEnabled)).ToArray();
            foreach (var query in queries)
            {
                query.Loading.Resolve(entity, query.MethodName);
                query.Projection?.Loading.Resolve(entity, query.MethodName);
            }

            ResolveProjectionReferences(entity, queries);
        }

        return new RepositoryDesign(
            entities.Values.OrderBy(entity => entity.EntityType.FullName).ToArray(),
            options, Array.AsReadOnly(typeAdapters.ToArray()), Array.AsReadOnly(inMemoryTranslations.ToArray()));
    }

    private static void ResolveProjectionReferences(RepositoryEntityDesign entity, IEnumerable<RepositoryQueryDesign> queries)
    {
        foreach (var query in queries)
        {
            RepositoryProjectionDesign[] matches;
            string reference;
            if (query.ProjectionTypeReference is not null)
            {
                matches = entity.Projections
                    .Where(projection => projection.ProjectionType == query.ProjectionTypeReference)
                    .ToArray();
                reference = $"type '{query.ProjectionTypeReference.FullName}'";
            }
            else if (query.ProjectionMethodNameReference is not null)
            {
                matches = entity.Projections
                    .Where(projection => string.Equals(
                        projection.MethodName,
                        query.ProjectionMethodNameReference,
                        StringComparison.Ordinal))
                    .ToArray();
                reference = $"method name '{query.ProjectionMethodNameReference}'";
            }
            else
            {
                continue;
            }

            if (matches.Length == 0)
            {
                throw new InvalidOperationException(
                    $"RGD1001: Query '{entity.EntityType.FullName}.{query.MethodName}' references a configured projection by {reference}, " +
                    "but no matching HasProjection was configured for that entity.");
            }

            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"RGD1002: Query '{entity.EntityType.FullName}.{query.MethodName}' references a configured projection by {reference}, " +
                    $"but {matches.Length} projections match. Reference a unique projection with ProjectTo(\"methodName\").");
            }

            query.Projection = matches[0];
        }
    }
}
