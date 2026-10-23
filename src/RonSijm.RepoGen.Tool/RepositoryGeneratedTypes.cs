namespace RonSijm.RepoGen.Tool;

/// <summary>Top-level generated CLR identities, including internal types and generic arity.</summary>
internal static class RepositoryGeneratedTypes
{
    internal static void ValidateNamespace(string? value)
    {
        if (value is null)
        {
            return;
        }

        if (value.Split('.').Any(static part => string.IsNullOrWhiteSpace(part) ||
                                                RepositoryModelFactory.EscapeIdentifier(part) != part || !char.IsLetter(part[0]) && part[0] != '_' ||
                                                part is "__arglist" or "__makeref" or "__refvalue" or "__reftype" ||
                                                part.Any(static character => !char.IsLetterOrDigit(character) && character != '_')))
        {
            throw new InvalidOperationException($"RGD1801: Invalid generated namespace '{value}'. Use an unescaped dotted C# namespace.");
        }

        if (value == "RonSijm.RepoGen.Abstractions" || value.StartsWith("RonSijm.RepoGen.Abstractions.", StringComparison.Ordinal) ||
            value == "RonSijm.RepoGen.EntityFrameworkCore" || value.StartsWith("RonSijm.RepoGen.EntityFrameworkCore.", StringComparison.Ordinal) ||
            value == "RonSijm.RepoGen.Testing" || value.StartsWith("RonSijm.RepoGen.Testing.", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"RGD1801: Generated namespace '{value}' is reserved for RepoGen runtime contracts.");
        }
    }

    internal static void Validate(IEnumerable<RepositoryGenerationModel> models)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var ports = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            foreach (var name in Names(model))
            {
                if (!owners.TryAdd(name, model.ContextTypeName))
                {
                    throw new InvalidOperationException($"RGG0007: Generated type '{name}' collides between '{owners[name]}' and '{model.ContextTypeName}'. Configure distinct UseGeneratedNamespace values and, for closed repositories, distinct contract namespaces.");
                }
            }

            foreach (var port in model.Entities.SelectMany(static entity => (entity.ReadPorts ?? []).Concat(entity.WritePorts ?? [])))
            {
                if (ports.TryGetValue(port.TypeName, out var previous) && previous != model.ContextTypeName)
                {
                    throw new InvalidOperationException($"RGG0009: Application port '{port.TypeName}' is bound by both '{previous}' and '{model.ContextTypeName}'. Generated scoped registrations cannot choose between contexts; use distinct ports or application-owned registration.");
                }
                else
                {
                    ports[port.TypeName] = model.ContextTypeName;
                }
            }
        }
    }

    internal static IEnumerable<string> Names(RepositoryGenerationModel model)
    {
        foreach (var name in ScriptedTestDoubleSourceRenderer.Names(model)) yield return name;
        var settings = model.Settings;
        string Infrastructure(string type) => settings.InfrastructureNamespace + "." + type;
        if (settings.UseDbContextResolver)
        {
            yield return Infrastructure("IDbContextResolver`1");
            yield return Infrastructure("ScopedDbContextResolver`1");
        }

        if (settings.GenerateCacheDecorator)
        {
            yield return Infrastructure("RepositoryCacheEntryOptions");
            yield return Infrastructure("IRepositoryCacheProvider");
        }

        if (model.Entities.Any(static entity => entity.GenerateSelectors))
        {
            foreach (var type in new[]
                     {
                         "BaseEntitySelector`1", "BaseCollectionSelector`1", "PagedResult`1", "CursorPagedResult`2",
                         "CursorPageRequest`1", "CursorPageEntry`2", "ParameterReplaceVisitor", "GroupedCollectionSelector`2"
                     })
            {
                yield return Infrastructure(type);
            }
        }

        var context = model.ContextTypeName.Split('.').Last();
        if (settings.CqrsSelectors)
        {
            yield return Infrastructure(context + "SelectorRepositoryRegistration");
        }

        if (settings.ContractNamespace is not null)
        {
            yield return settings.ContractNamespace + "." + context + "RepositoryRegistration";
        }

        foreach (var entity in model.Entities)
        {
            string Persistence(string type) => (entity.Namespace.Length == 0 ? "" : entity.Namespace + ".") + entity.EntityName + type;
            string Port(string prefix) => (entity.Namespace.Length == 0 ? "" : entity.Namespace + ".") + prefix + entity.EntityName + "Repository";
            yield return Persistence("Repository");
            yield return Port("I");
            if (settings.GenerateCacheDecorator)
            {
                yield return Persistence("RepositoryCacheDecorator");
                yield return Port("ICacheable");
            }

            if (entity.GenerateSelectors)
            {
                foreach (var type in new[] { "Selector", "CollectionSelector", "SelectorExtensions", "CollectionSelectorExtensions" })
                {
                    yield return Persistence(type);
                }
            }
            else if (entity.SortableFields.Count > 0)
            {
                yield return Persistence("DynamicSortingExtensions");
            }

            foreach (var ns in entity.Projections.Select(static projection => projection.Namespace).Distinct(StringComparer.Ordinal))
            {
                yield return (ns.Length == 0 ? "" : ns + ".") + entity.EntityName + "ConfiguredProjectionExtensions";
            }

            foreach (var ns in entity.Queries.Where(static query => query.RegisteredRoot is null).Select(static query => query.Namespace).Distinct(StringComparer.Ordinal))
            {
                yield return (ns.Length == 0 ? "" : ns + ".") + entity.EntityName + "ConfiguredQueryExtensions";
            }

            if (settings.CqrsSelectors)
            {
                if (entity.WriteEnabled)
                {
                    yield return Port("IWrite");
                }

                if (entity.ReadEnabled)
                {
                    yield return Port("IRead");
                    yield return Persistence("ReadRepository");
                    yield return (entity.Namespace.Length == 0 ? "" : entity.Namespace + ".") + "Read" + entity.EntityName + "Selector";
                    yield return (entity.Namespace.Length == 0 ? "" : entity.Namespace + ".") + "Read" + entity.EntityName + "CollectionSelector";
                }
            }

            if (settings.ContractNamespace is not null)
            {
                yield return Persistence("RepositoryOperations");
                foreach (var type in ClosedRepositorySourceRenderer.ContractNames(entity))
                {
                    yield return settings.ContractNamespace + "." + type;
                }

                if (entity.ReadEnabled || entity.WriteEnabled)
                {
                    yield return settings.ContractNamespace + ".I" + entity.EntityName + "Repository";
                }
            }
        }
    }
}
