using System.Linq.Expressions;
using System.Text;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static class ClosedRepositorySourceRenderer
{
    public static void ValidateEntity(IEntityType entity, RepositoryEntityDesign? design, bool cachingEnabled)
    {
        if (design is null)
        {
            return;
        }

        var forbidden = entity.Model.GetEntityTypes().Select(static item => item.ClrType).ToHashSet();
        foreach (var port in design.ReadPorts.Concat(design.WritePorts))
        {
            foreach (var method in port.GetInterfaces().Append(port).SelectMany(static type => type.GetMethods()))
            {
                var boundary = new RepositoryContractBoundary(forbidden);
                boundary.Validate(method.ReturnType, port.FullName + "." + method.Name + ".return");
                foreach (var parameter in method.GetParameters())
                {
                    boundary.Validate(parameter.ParameterType, port.FullName + "." + method.Name + "[" + parameter.Name + "]");
                }
            }
        }

        foreach (var query in design.Queries.Where(query => query.Target == RepositoryQueryTarget.Write ? design.WriteEnabled : design.ReadEnabled))
        {
            ValidateName(query.MethodName);
            if (cachingEnabled && query.Cache.Enabled)
            {
                if (query.Target == RepositoryQueryTarget.Write)
                {
                    throw new InvalidOperationException($"RGD2103: Command-side query '{query.MethodName}' cannot enable caching. Loading command state must execute against its selected context, not return a cached detached result.");
                }

                throw new InvalidOperationException($"RGD1203: Closed query '{query.MethodName}' configures caching. UseClosedCaching requires a scope-aware commit-coordinated adapter. Configure it explicitly, or remove Cache().");
            }

            if (query.Dictionary is not null)
            {
                ValidateContractType(query.Dictionary.KeySelector.ReturnType, forbidden, query.MethodName + ".dictionaryKey");
                ValidateContractType(query.Dictionary.ValueSelector.ReturnType, forbidden, query.MethodName + ".dictionaryValue");
            }
            else if (query.Scalar is not null)
            {
                ValidateContractType(query.Scalar.ReturnType, forbidden, query.MethodName + ".return");
            }
            else if (query.ResultKind is not RepositoryQueryResultKind.Count and not RepositoryQueryResultKind.Exists)
            {
                var result = query.ResultStages.Count > 0 ? query.ResultStages[^1].OutputType : query.RegisteredSource?.ResultType ?? query.Source?.ResultType ?? query.Group?.ResultType ?? query.Projection?.ProjectionType ?? entity.ClrType;
                if (ProjectionExpressionRenderer.IsAnonymousType(result))
                {
                    throw new InvalidOperationException($"RGD1502: Query '{query.MethodName}' has an anonymous final result. Select a named DTO or scalar before choosing its terminal.");
                }

                ValidateContractType(result, forbidden, query.MethodName + ".return");
            }

            ValidateParameters(query.Predicates, forbidden);
            ValidateParameters(query.Memberships.Concat(query.ResultStages.Select(static stage => stage.Membership).OfType<RepositoryMembershipDesign>())
                .Select(static membership => membership.Values), forbidden, skipRow: false);
            if (query.ContextKey is not null)
            {
                ValidateParameters([query.ContextKey], forbidden, skipRow: false);
            }

            ValidateParameters(query.Validations.Select(static validation => validation.Predicate), forbidden, skipRow: false);
            ValidateParameters(query.CursorValidations.Select(static validation => validation.Predicate), forbidden);
            if (query.Group is not null)
            {
                ValidateParameters([query.Group.KeySelector, query.Group.Projection], forbidden);
            }

            ValidateParameters(query.Includes.Concat(query.Projection?.Includes ?? []).SelectMany(static include =>
                new[] { include.Navigation }.Concat(include.ThenIncludes)), forbidden);
            ValidateParameters(query.Sorts.Select(static sort => sort.KeySelector), forbidden);
            if (query.Dictionary is { } terminalDictionary)
            {
                ValidateParameters([terminalDictionary.KeySelector, terminalDictionary.ValueSelector], forbidden);
            }

            if (query.Scalar is { } terminalScalar)
            {
                ValidateParameters([terminalScalar.Selector], forbidden);
            }

            ValidateParameters(query.ConditionalPredicates.Select(static predicate => predicate.Predicate), forbidden);
            ValidateParameters(query.ConditionalPredicates.Select(static predicate => predicate.Condition), forbidden, skipRow: false);
            if (query.Source is not null)
            {
                ValidateParameters([query.Source.Expression], forbidden);
            }

            if (query.RegisteredSource is not null)
            {
                ValidateParameters([query.RegisteredSource.RequestBinding], forbidden, skipRow: false);
            }

            foreach (var stage in query.ResultStages)
            {
                if (stage.Expression is not null)
                {
                    ValidateParameters([stage.Expression], forbidden, skipRow: stage.Kind is not RepositoryQueryStageKind.Skip and not RepositoryQueryStageKind.Take);
                }

                if (stage.Condition is not null)
                {
                    ValidateParameters([stage.Condition], forbidden, skipRow: false);
                }

                if (stage.Projection is not null)
                {
                    ValidateParameters([stage.Projection], forbidden);
                }

                if (stage.FieldSort is { } fieldSort)
                {
                    ValidateParameters([fieldSort.FieldName, fieldSort.Descending], forbidden, skipRow: false);
                }
            }

            if (query.Cursor is not null)
            {
                ValidateContractType(query.Cursor.CursorType, forbidden, query.MethodName + ".cursor");
                foreach (var parameter in query.Cursor.SeekPredicate.Parameters.Skip(2))
                {
                    ValidateContractType(parameter.Type, forbidden, "request[" + parameter.Name + "]");
                    ValidateParameterName(RepositoryModelFactory.ToParameterName(parameter.Name ?? "value"));
                }

                ValidateParameters([query.Cursor.CursorSelector], forbidden);
            }
        }

        foreach (var command in design.Commands.Where(_ => design.WriteEnabled))
        {
            if (entity.FindPrimaryKey() is null)
            {
                throw new InvalidOperationException($"Keyless entity '{entity.ClrType.FullName}' cannot configure write commands.");
            }

            ValidateName(command.MethodName);
            ValidateParameters(command.Predicates.Concat(command.Updates.Select(static update => update.Value)), forbidden);
            ValidateParameters(command.Updates.Where(static update => update.Condition is not null).Select(static update => update.Condition!), forbidden, skipRow: false);
            if (command.CreateMapping is not null)
            {
                ValidateParameters([command.CreateMapping], forbidden, skipRow: false);
            }

            if (command.ResultProjection is not null)
            {
                ValidateParameters([command.ResultProjection], forbidden);
                ValidateContractType(command.ResultProjection.ReturnType, forbidden, command.MethodName + ".return");
            }

            ValidateParameters(command.ExpectedVersions.Select(static version => version.Value), forbidden);
            if (command.Batch is { } batch)
            {
                ValidateParameters([batch.Items], forbidden, skipRow: false);
                ValidateContractType(batch.ItemKey.Parameters[0].Type, forbidden, command.MethodName + ".batchItem");
            }
            if (command.CreateMany is { } many)
            {
                ValidateParameters([many.Items], forbidden, skipRow: false);
                ValidateContractType(many.Mapping.Parameters[0].Type, forbidden, command.MethodName + ".createItem");
            }

            foreach (var collection in command.Collections)
            {
                ValidateParameters([collection.Items], forbidden, skipRow: false);
                ValidateContractType(collection.ItemKey.Parameters[0].Type, forbidden, command.MethodName + ".collectionItem");
                if (collection.Condition is not null)
                {
                    ValidateParameters([collection.Condition], forbidden, skipRow: false);
                }
            }
            if (command.ContextKey is not null)
            {
                ValidateParameters([command.ContextKey], forbidden, skipRow: false);
            }

            ValidateParameters(command.Validations.Select(static validation => validation.Predicate), forbidden, skipRow: false);
        }
    }

    private static void ValidateParameters(IEnumerable<LambdaExpression> expressions, HashSet<Type> forbidden, bool skipRow = true)
    {
        foreach (var parameter in expressions.SelectMany(expression => expression.Parameters.Skip(skipRow ? 1 : 0)))
        {
            ValidateContractType(parameter.Type, forbidden, "request[" + parameter.Name + "]");
            ValidateParameterName(RepositoryModelFactory.ToParameterName(parameter.Name ?? "value"));
        }
    }

    public static void ValidateOperationParameters(EntityRepositoryModel entity)
    {
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signature in entity.Queries.Select(RepositoryOperationSignature.Query).Concat(entity.Commands.Select(RepositoryOperationSignature.Command)))
        {
            if (!signatures.Add(signature))
            {
                throw new InvalidOperationException($"RGD1004: Duplicate closed operation '{signature}'. Use distinct input types or names; return types/reference nullability do not distinguish overloads.");
            }
        }

        foreach (var parameter in entity.Queries.SelectMany(static query => query.Parameters).Concat(entity.Commands.SelectMany(static command => command.Parameters)))
        {
            ValidateParameterName(parameter.Name);
        }

        foreach (var query in entity.Queries.Where(static query => query.Target == RepositoryQueryTarget.Write))
        {
            ValidateName(query.MethodName);
            if (query.MethodName == "SaveChangesAsync" && ProjectionSourceRenderer.CreateQueryParameters(query).Count == 0)
            {
                throw new InvalidOperationException("RGD2104: Command-side query 'SaveChangesAsync' conflicts with the shared context save operation. Choose a distinct query name.");
            }
        }

        if (entity.Commands.Any(static command => command.MethodName == "SaveChangesAsync" && command.Parameters.Count == 0))
        {
            throw new InvalidOperationException("Command name 'SaveChangesAsync' conflicts with the shared context save operation. Use a specific operation name.");
        }
    }

    internal static void ValidateParameterName(string name)
    {
        if (name.TrimStart('@').StartsWith("__repoGen", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Operation parameter '{name}' uses the reserved generated-local prefix '__repoGen'. Rename the lambda parameter.");
        }

        if (IsInfrastructureParameterName(name))
        {
            throw new InvalidOperationException($"Operation parameter '{name}' conflicts with generated infrastructure. Rename the lambda parameter.");
        }
    }

    internal static bool IsInfrastructureParameterName(string name) => name.TrimStart('@').StartsWith("__repoGen", StringComparison.Ordinal) || name.TrimStart('@') is
        "cancellationToken" or "page" or "pageSize" or "cursorPage" or "context" or "lease" or "repository" or "result" or
        "contextSource" or "unitOfWork" or "entity" or "selector" or "query" or "setters" or "source" or "token" or "item" or "cursor" or "cache" or "cacheKey" or "cacheableRepository";

    private static void ValidateName(string name)
    {
        var asyncName = name.EndsWith("Async", StringComparison.Ordinal) ? name : name + "Async";
        if (asyncName.Length == 0 || (!char.IsLetter(asyncName[0]) && asyncName[0] != '_') ||
            asyncName.Any(static character => !char.IsLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException($"RGD1005: Closed operation name '{name}' is not a valid identifier.");
        }
    }

    private static void ValidateContractType(Type type, HashSet<Type> forbidden, string path)
    {
        new RepositoryContractBoundary(forbidden).Validate(type, path);
    }

    public static string RenderContracts(RepositoryGenerationModel model, string? selectedContract = null)
    {
        var ns = model.Settings.ContractNamespace ?? throw new InvalidOperationException("UseClosedRepositories is required for contract output.");
        if (ns.Split('.').Any(static part => string.IsNullOrWhiteSpace(part) || RepositoryModelFactory.EscapeIdentifier(part) != part ||
                                             !char.IsLetter(part[0]) && part[0] != '_' || part.Any(static character => !char.IsLetterOrDigit(character) && character != '_')))
        {
            throw new InvalidOperationException($"Invalid contract namespace '{ns}'.");
        }

        var builder = new StringBuilder("// <auto-generated/>\n#nullable enable\n\nnamespace " + ns + ";\n");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in model.Entities)
        {
            if (entity.Namespace == ns)
            {
                throw new InvalidOperationException($"Contract namespace '{ns}' collides with persistence repository names. Use a separate Application/Contracts namespace.");
            }

            if (!names.Add(entity.EntityName))
            {
                throw new InvalidOperationException($"Entity name '{entity.EntityName}' occurs more than once in contract namespace '{ns}'.");
            }

            foreach (var port in (entity.ReadPorts ?? []).Concat(entity.WritePorts ?? []).DistinctBy(static port => port.TypeName))
            {
                if (!ports.Add(port.TypeName))
                {
                    throw new InvalidOperationException($"Application port '{port.TypeName}' is bound by several entities. Bind each port to one implementation.");
                }

                var generatedNames = ContractNames(entity).Append("I" + entity.EntityName + "Repository")
                    .Select(name => "global::" + ns + "." + name);
                if (generatedNames.Contains(port.TypeName, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"Bound port '{port.TypeName}' collides with a generated facade name. Use a separate namespace for generated contracts.");
                }
            }

            if (entity.ReadEnabled && (selectedContract is null || selectedContract == "IRead" + entity.EntityName + "Repository"))
            {
                builder.AppendLine().Append("public interface IRead").Append(entity.EntityName).Append("Repository");
                AppendPorts(builder, entity.ReadPorts);
                builder.AppendLine("{");
                foreach (var query in entity.Queries.Where(static query => query.Target == RepositoryQueryTarget.Read))
                {
                    if (entity.ReadPorts?.Any(port => port.OperationSignatures.Contains(RepositoryOperationSignature.Query(query), StringComparer.Ordinal)) == true)
                    {
                        continue;
                    }

                    AppendQuerySignature(builder, query, "    ", implementation: false);
                }

                builder.AppendLine("}");
            }

            if (entity.WriteEnabled && (selectedContract is null || selectedContract == "IWrite" + entity.EntityName + "Repository"))
            {
                builder.AppendLine().Append("public interface IWrite").Append(entity.EntityName).Append("Repository");
                AppendPorts(builder, entity.WritePorts);
                builder.AppendLine("{");
                foreach (var query in entity.Queries.Where(static query => query.Target == RepositoryQueryTarget.Write))
                {
                    if (entity.WritePorts?.Any(port => port.OperationSignatures.Contains(RepositoryOperationSignature.Query(query), StringComparer.Ordinal)) == true)
                    {
                        continue;
                    }

                    AppendQuerySignature(builder, query, "    ", implementation: false);
                }

                foreach (var command in entity.Commands)
                {
                    if (entity.WritePorts?.Any(port => port.OperationSignatures.Contains(RepositoryOperationSignature.Command(command), StringComparer.Ordinal)) == true)
                    {
                        continue;
                    }

                    AppendCommandSignature(builder, command, "    ", implementation: false);
                }

                builder.AppendLine("}");
            }

            if ((entity.ReadEnabled || entity.WriteEnabled) && (selectedContract is null || selectedContract == "I" + entity.EntityName + "Repository"))
            {
                builder.AppendLine().Append("public interface I").Append(entity.EntityName).Append("Repository : ");
                builder.Append(string.Join(", ", ContractNames(entity))).AppendLine(" { }");
            }
        }

        return builder.ToString();
    }

    private static void AppendPorts(StringBuilder builder, IReadOnlyList<BoundPortModel>? ports)
    {
        if (ports?.Count > 0)
        {
            builder.Append(" : ").Append(string.Join(", ", ports.Select(static port => port.TypeName)));
        }

        builder.AppendLine();
    }

    public static string RenderImplementation(RepositoryGenerationModel model, bool registration = true)
    {
        var builder = new StringBuilder("// <auto-generated/>\n#nullable enable\n");
        foreach (var entity in model.Entities.Where(static entity => entity.ReadEnabled || entity.WriteEnabled))
        {
            var ns = model.Settings.ContractNamespace!;
            builder.AppendLine();
            if (!string.IsNullOrEmpty(entity.Namespace))
            {
                builder.Append("namespace ").Append(entity.Namespace).AppendLine("\n{");
            }

            builder.Append("    internal sealed class ").Append(entity.EntityName).Append("RepositoryOperations : global::")
                .Append(ns).Append(".I").Append(entity.EntityName).AppendLine("Repository");
            builder.AppendLine("    {");
            var providerType = model.Settings.UseAsyncContextLeases
                ? model.Settings.ContextKeyTypeName is null
                    ? "global::RonSijm.RepoGen.EntityFrameworkCore.IDbContextLeaseProvider<" + entity.ContextTypeName + ">"
                    : "global::RonSijm.RepoGen.EntityFrameworkCore.IKeyedDbContextLeaseProvider<" + entity.ContextTypeName + ", " + model.Settings.ContextKeyTypeName + ">"
                : entity.ContextTypeName;
            builder.Append("        private readonly ").Append(providerType).AppendLine(" contextSource;");
            if (model.Settings.ClosedCaching)
            {
                builder.AppendLine("        private readonly global::RonSijm.RepoGen.Abstractions.IRepositoryResultCache cache;");
            }

            if (entity.Commands.Any(static command => command.IsAtomic))
            {
                builder.Append("        private readonly ").Append(model.Settings.AtomicWriterTypeName).AppendLine(" __repoGenAtomicWriter;");
            }

            if (entity.Commands.Any(static command => command.IsReturning))
            {
                builder.Append("        private readonly ").Append(model.Settings.ReturningWriterTypeName).AppendLine(" __repoGenReturningWriter;");
            }

            foreach (var root in entity.Queries.Select(static query => query.RegisteredRoot).OfType<ConfiguredRegisteredQueryModel>().DistinctBy(static root => root.FieldName))
            {
                builder.Append("        private readonly ").Append(root.ProviderTypeName).Append(' ').Append(root.FieldName).AppendLine(";");
            }

            builder.Append("        public ").Append(entity.EntityName).Append("RepositoryOperations(").Append(providerType).Append(" contextSource")
                .Append(model.Settings.ClosedCaching ? ", global::RonSijm.RepoGen.Abstractions.IRepositoryResultCache cache" : "");
            if (entity.Commands.Any(static command => command.IsAtomic))
            {
                builder.Append(", ").Append(model.Settings.AtomicWriterTypeName).Append(" __repoGenAtomicWriter");
            }

            if (entity.Commands.Any(static command => command.IsReturning))
            {
                builder.Append(", ").Append(model.Settings.ReturningWriterTypeName).Append(" __repoGenReturningWriter");
            }

            foreach (var root in entity.Queries.Select(static query => query.RegisteredRoot).OfType<ConfiguredRegisteredQueryModel>().DistinctBy(static root => root.FieldName))
            {
                builder.Append(", ").Append(root.ProviderTypeName).Append(' ').Append(root.FieldName);
            }

            builder.AppendLine(")");
            builder.AppendLine("        {");
            builder.AppendLine("            this.contextSource = contextSource ?? throw new global::System.ArgumentNullException(nameof(contextSource));");
            if (model.Settings.ClosedCaching)
            {
                builder.AppendLine("            this.cache = cache ?? throw new global::System.ArgumentNullException(nameof(cache));");
            }

            if (entity.Commands.Any(static command => command.IsAtomic))
            {
                builder.AppendLine("            this.__repoGenAtomicWriter = __repoGenAtomicWriter ?? throw new global::System.ArgumentNullException(nameof(__repoGenAtomicWriter));");
            }

            if (entity.Commands.Any(static command => command.IsReturning))
            {
                builder.AppendLine("            this.__repoGenReturningWriter = __repoGenReturningWriter ?? throw new global::System.ArgumentNullException(nameof(__repoGenReturningWriter));");
            }

            foreach (var root in entity.Queries.Select(static query => query.RegisteredRoot).OfType<ConfiguredRegisteredQueryModel>().DistinctBy(static root => root.FieldName))
            {
                builder.Append("            this.").Append(root.FieldName).Append(" = ").Append(root.FieldName).Append(" ?? throw new global::System.ArgumentNullException(nameof(").Append(root.FieldName).AppendLine("));");
            }

            builder.AppendLine("        }");

            // The model already omits queries whose selected read/write capability is disabled.
            foreach (var query in entity.Queries)
            {
                AppendQueryImplementation(builder, entity, query, model.Settings);
            }

            foreach (var command in entity.Commands.Where(_ => entity.WriteEnabled))
            {
                AppendCommandImplementation(builder, entity, command, model.Settings);
            }

            if (entity.Queries.Any(static query => query.RegisteredRoot is not null && query.Pipeline?.Stages.Any(static stage => stage.Kind is RepositoryQueryStageKind.ThenBy or RepositoryQueryStageKind.ThenByDescending) == true))
            {
                ResultPipelineSourceRenderer.AppendOrderingHelper(builder, "        ",
                    entity.Queries.Any(static query => query.RegisteredRoot is not null && query.Pipeline?.Stages.Any(static stage => stage.FieldSort is not null) == true));
            }

            builder.AppendLine("    }");
            if (!string.IsNullOrEmpty(entity.Namespace))
            {
                builder.AppendLine("}");
            }
        }

        if (registration)
        {
            AppendRegistration(builder, model);
        }

        return builder.ToString();
    }

    internal static IEnumerable<string> ContractNames(EntityRepositoryModel entity)
    {
        if (entity.ReadEnabled)
        {
            yield return "IRead" + entity.EntityName + "Repository";
        }

        if (entity.WriteEnabled)
        {
            yield return "IWrite" + entity.EntityName + "Repository";
        }
    }

    internal static string ContractType(string type, string infrastructurePrefix = "global::RonSijm.RepoGen.") => type
        .Replace(infrastructurePrefix + "PagedResult<", "global::RonSijm.RepoGen.Abstractions.PagedResult<", StringComparison.Ordinal)
        .Replace(infrastructurePrefix + "CursorPagedResult<", "global::RonSijm.RepoGen.Abstractions.CursorPagedResult<", StringComparison.Ordinal)
        .Replace(infrastructurePrefix + "CursorPageRequest<", "global::RonSijm.RepoGen.Abstractions.CursorPageRequest<", StringComparison.Ordinal);

    private static void AppendQuerySignature(StringBuilder builder, ConfiguredQueryModel query, string indent, bool implementation)
    {
        var stream = query.ResultKind == RepositoryQueryResultKind.Stream;
        builder.AppendLine().Append(indent).Append(implementation ? "public async " : string.Empty)
            .Append(ContractType(ProjectionSourceRenderer.RenderQueryReturnType(query), query.InfrastructurePrefix)).Append(' ').Append(query.MethodName).AppendLine("(");
        foreach (var parameter in ProjectionSourceRenderer.CreateQueryParameters(query))
        {
            builder.Append(indent).Append("    ").Append(ContractType(parameter.Type, query.InfrastructurePrefix)).Append(' ').Append(parameter.Name).AppendLine(",");
        }

        builder.Append(indent).Append("    ");
        if (implementation && stream)
        {
            builder.Append("[global::System.Runtime.CompilerServices.EnumeratorCancellation] ");
        }

        builder.Append("global::System.Threading.CancellationToken cancellationToken = default)").AppendLine(implementation ? string.Empty : ";");
    }

    private static void AppendCommandSignature(StringBuilder builder, ConfiguredCommandModel command, string indent, bool implementation)
    {
        builder.AppendLine().Append(indent).Append(implementation ? "public async " : string.Empty)
            .Append(command.ReturnTypeName).Append(' ').Append(command.MethodName).AppendLine("(");
        foreach (var parameter in command.Parameters)
        {
            builder.Append(indent).Append("    ").Append(parameter.TypeName).Append(' ').Append(parameter.Name).AppendLine(",");
        }

        builder.Append(indent).Append("    global::System.Threading.CancellationToken cancellationToken = default)")
            .AppendLine(implementation ? string.Empty : ";");
    }

    private static void AppendContext(StringBuilder builder, EntityRepositoryModel entity, RepositoryGenerationSettingsModel settings, string? key = null, bool createRepository = true,
        ConfiguredCommandTimeoutModel? commandTimeout = null)
    {
        if (settings.UseAsyncContextLeases)
        {
            builder.Append("            await using var lease = await contextSource.AcquireAsync(").Append(key is null ? "" : key + ", ")
                .AppendLine("cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            var context = lease.Context;");
        }
        else
        {
            builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
            builder.AppendLine("            var context = contextSource;");
        }

        if (createRepository)
        {
            builder.Append("            var repository = new ").Append(entity.EntityName).AppendLine("Repository(context);");
        }

        if ((commandTimeout is null ? settings.CommandTimeoutSeconds : commandTimeout.Seconds) is { } timeout)
        {
            builder.Append("            using var __repoGenTimeout = new global::RonSijm.RepoGen.EntityFrameworkCore.RepositoryCommandTimeoutScope(context, ").Append(timeout).AppendLine(");");
        }

        if (settings.ClosedCaching)
        {
            var fallback = settings.SingleContextCacheScope is null ? null : ProjectionExpressionRenderer.RenderBody(Expression.Lambda(Expression.Constant(settings.SingleContextCacheScope)), []);
            builder.Append("            var __repoGenScope = ").Append(settings.UseAsyncContextLeases ? "lease.ScopeKey ?? " : "")
                .Append(fallback ?? "throw new global::System.InvalidOperationException(\"Cached context leases must supply an application-owned ScopeKey.\")").AppendLine(";");
            builder.AppendLine("            global::RonSijm.RepoGen.EntityFrameworkCore.RepositoryCacheCoordinator.Attach(cache, contextSource, __repoGenScope);");
        }
    }

    private static void AppendQueryImplementation(StringBuilder builder, EntityRepositoryModel entity, ConfiguredQueryModel query, RepositoryGenerationSettingsModel settings)
    {
        AppendQuerySignature(builder, query, "        ", implementation: true);
        builder.AppendLine("        {");
        AppendValidations(builder, query.Validations);
        RepositoryQueryPreparationSourceRenderer.AppendPreparation(builder, query, "            ");
        AppendContext(builder, entity, settings, query.ContextKeySource, commandTimeout: query.CommandTimeout);
        var cached = settings.ClosedCaching && query.Cache.Enabled;
        var arguments = ProjectionSourceRenderer.CreateQueryParameters(query).Select(parameter =>
            parameter.Name == "cursorPage"
                ? $"(cursorPage.HasCursor ? {query.InfrastructurePrefix}CursorPageRequest<{query.Cursor!.CursorTypeName}>.After(cursorPage.Cursor!, cursorPage.PageSize) : {query.InfrastructurePrefix}CursorPageRequest<{query.Cursor!.CursorTypeName}>.First(cursorPage.PageSize))"
                : parameter.Name).Concat(RepositoryQueryPreparationSourceRenderer.Arguments(query)).Append(cached ? "__repoGenToken" : "cancellationToken");
        var call = "global::" + (string.IsNullOrEmpty(query.Namespace) ? string.Empty : query.Namespace + ".") +
                   entity.EntityName + "ConfiguredQueryExtensions." + query.MethodName + (RepositoryQueryPreparationSourceRenderer.Required(query) ? "Prepared" : "") + "(repository, " + string.Join(", ", arguments) + ")";
        if (cached)
        {
            var resultType = ContractType(ProjectionSourceRenderer.RenderQueryReturnType(query), query.InfrastructurePrefix);
            resultType = resultType["global::System.Threading.Tasks.Task<".Length..^1];
            builder.Append("            return await global::RonSijm.RepoGen.EntityFrameworkCore.RepositoryCacheCoordinator.ReadAsync<").Append(resultType).AppendLine(">(");
            var shape = global::System.Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(global::System.Text.Json.JsonSerializer.Serialize(query))));
            builder.Append("                cache, context, __repoGenScope, \"").Append(entity.EntityTypeName).Append('.').Append(query.MethodName).Append(':').Append(shape).AppendLine("\",");
            builder.Append("                new object?[] { ").Append(string.Join(", ", ProjectionSourceRenderer.CreateQueryParameters(query).Select(static parameter => parameter.Name)
                .Concat(RepositoryQueryPreparationSourceRenderer.Arguments(query)))).AppendLine(" },");
            builder.Append("                new string[] { ").Append(string.Join(", ", query.Cache.DependentEntityTypeNames.Select(static tag => tag.Replace("global::", "", StringComparison.Ordinal))
                .Prepend(entity.CacheTag).Distinct(StringComparer.Ordinal).Select(static tag => "\"" + tag + "\""))).AppendLine(" },");
            builder.Append("                new global::RonSijm.RepoGen.Abstractions.RepositoryCacheOptions(")
                .Append(query.Cache.AbsoluteExpirationTicks is { } absolute ? "global::System.TimeSpan.FromTicks(" + absolute + "L)" : "null").Append(", ")
                .Append(query.Cache.SlidingExpirationTicks is { } sliding ? "global::System.TimeSpan.FromTicks(" + sliding + "L)" : "null").AppendLine("),");
            builder.AppendLine("                async __repoGenToken =>\n                {");
        }

        if (query.RegisteredRoot is not null)
        {
            var token = cached ? "__repoGenToken" : "cancellationToken";
            if (query.PageConsistency != RepositoryPageConsistency.BestEffort)
            {
                builder.Append("            await using var __repoGenReadTransaction = await global::RonSijm.RepoGen.EntityFrameworkCore.RepositoryReadTransaction.AcquireAsync(context, ").Append(token).AppendLine(").ConfigureAwait(false);");
            }

            ResultPipelineSourceRenderer.Append(builder, query, "            ", directRoot: true, tokenName: token);
        }
        else if (query.ResultKind == RepositoryQueryResultKind.Stream)
        {
            builder.Append("            await foreach (var item in ").Append(call).AppendLine(")");
            builder.AppendLine("            {\n                cancellationToken.ThrowIfCancellationRequested();\n                yield return item;\n            }");
            builder.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
        }
        else if (query.ResultKind == RepositoryQueryResultKind.Paged)
        {
            builder.Append("            var result = await ").Append(call).AppendLine(".ConfigureAwait(false);");
            builder.AppendLine("            return new(result.Items, result.TotalCount, result.Page, result.PageSize);");
        }
        else if (query.ResultKind == RepositoryQueryResultKind.CursorPaged)
        {
            builder.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(cursorPage);");
            builder.Append("            var result = await ").Append(call).AppendLine(".ConfigureAwait(false);");
            builder.AppendLine("            return new(result.Items, result.HasMore, result.NextCursor, cursorPage.PageSize);");
        }
        else
        {
            builder.Append("            return await ").Append(call).AppendLine(".ConfigureAwait(false);");
        }

        if (cached)
        {
            builder.AppendLine("                }, cancellationToken).ConfigureAwait(false);");
        }

        builder.AppendLine("        }");
    }

    private static void AppendCommandImplementation(StringBuilder builder, EntityRepositoryModel entity, ConfiguredCommandModel command, RepositoryGenerationSettingsModel settings)
    {
        AppendCommandSignature(builder, command, "        ", implementation: true);
        builder.AppendLine("        {");
        AppendValidations(builder, command.Validations);
        CollectionReconciliationSourceRenderer.AppendInputs(builder, command, "            ");
        BatchCommandSourceRenderer.AppendInputs(builder, command, "            ");
        CreateManyCommandSourceRenderer.AppendInputs(builder, command, "            ");
        AppendContext(builder, entity, settings, command.ContextKeySource, createRepository: !command.IsReturning, commandTimeout: command.CommandTimeout);
        if (settings.ClosedCaching)
        {
            builder.AppendLine("            global::RonSijm.RepoGen.EntityFrameworkCore.RepositoryCacheCoordinator.ValidateWriteBoundary(contextSource, context);");
            builder.Append("            var __repoGenWriteTags = global::RonSijm.RepoGen.EntityFrameworkCore.RepositoryCacheCoordinator.WriteTags(context, new string[] { ")
                .Append(string.Join(", ", entity.CacheInvalidationTags.Select(static tag => "\"" + tag + "\""))).AppendLine(" });");
        }

        if (command.IsReturning)
        {
            ReturningCommandSourceRenderer.AppendBody(builder, entity, command, settings);
            builder.AppendLine("        }");
            return;
        }

        if (command.IsAtomic)
        {
            AtomicCommandSourceRenderer.AppendBody(builder, entity, command, settings);
            builder.AppendLine("        }");
            return;
        }

        if (command.IsTracked)
        {
            if (!command.SaveChanges && settings.UseAsyncContextLeases)
            {
                builder.AppendLine("            if (lease.OwnsContext) throw new global::System.InvalidOperationException(\"A staged command requires a borrowed context or shared unit of work, not an operation-owned context.\");");
            }

            if (settings.ClosedCaching)
            {
                builder.AppendLine("            try\n            {");
            }

            TrackedCommandSourceRenderer.AppendBody(builder, entity, command, "            ",
                "(object)contextSource as global::RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork", returnResult: !settings.ClosedCaching, inputsPrepared: true);
            if (settings.ClosedCaching)
            {
                builder.AppendLine("            if (result.Succeeded && result.AffectedRows > 0 && (object)contextSource is not global::RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork)");
                AppendCacheWrite(builder);
                builder.Append("            return result").Append(command.ResultTypeName is null ? ".WithoutValue()" : "").AppendLine(";");
                builder.AppendLine("            }\n            catch (global::RonSijm.RepoGen.Abstractions.RepositorySavedResultException)\n            {");
                builder.AppendLine("                if ((object)contextSource is not global::RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork)");
                AppendCacheWrite(builder);
                builder.AppendLine("                throw;\n            }");
            }

            builder.AppendLine("        }");
            return;
        }

        builder.Append("            var result = await repository.").Append(command.MethodName).Append('(')
            .Append(string.Join(", ", command.Parameters.Select(static parameter => parameter.Name).Append("cancellationToken"))).AppendLine(").ConfigureAwait(false);");
        if (settings.ClosedCaching)
        {
            builder.AppendLine(command.WithOutcome ? "            if (result.Succeeded && result.AffectedRows > 0)" : "            if (result > 0)");
            AppendCacheWrite(builder);
        }
        else if (settings.UseAsyncContextLeases)
        {
            builder.AppendLine(command.WithOutcome
                ? "            if (result.Succeeded && result.AffectedRows > 0 && contextSource is global::RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork unitOfWork)"
                : "            if (result > 0 && contextSource is global::RonSijm.RepoGen.Abstractions.IRepositoryUnitOfWork unitOfWork)");
            builder.AppendLine("            {");
            builder.Append("                await unitOfWork.RecordChangesAsync(new string[] { ")
                .Append(string.Join(", ", entity.CacheInvalidationTags.Select(static tag => "\"" + tag + "\"")))
                .AppendLine(" }, cancellationToken).ConfigureAwait(false);");
            builder.AppendLine("            }");
        }

        builder.AppendLine("            return result;\n        }");
    }

    private static void AppendCacheWrite(StringBuilder builder) => builder.AppendLine("                await global::RonSijm.RepoGen.EntityFrameworkCore.RepositoryCacheCoordinator.WrittenAsync(cache, contextSource, context, __repoGenScope, __repoGenWriteTags, cancellationToken).ConfigureAwait(false);");

    internal static void AppendValidations(StringBuilder builder, IReadOnlyList<ConfiguredInputValidationModel>? validations, string indent = "            ")
    {
        foreach (var validation in validations ?? [])
        {
            builder.Append(indent).Append("if (!(").Append(validation.PredicateSource).Append(")) throw new global::System.ArgumentException(").Append(validation.MessageSource).AppendLine(");");
        }
    }

    internal static void AppendRegistration(StringBuilder builder, RepositoryGenerationModel model)
    {
        var contextName = model.ContextTypeName.Split('.').Last();
        builder.AppendLine().Append("namespace ").Append(model.Settings.ContractNamespace).AppendLine("\n{");
        builder.Append("    public static class ").Append(contextName).AppendLine("RepositoryRegistration");
        builder.AppendLine("    {");
        builder.Append("        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection Add")
            .Append(contextName).AppendLine("Repositories(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        builder.AppendLine("        {\n            global::System.ArgumentNullException.ThrowIfNull(services);");
        foreach (var entity in model.Entities.Where(static entity => entity.ReadEnabled || entity.WriteEnabled))
        {
            var operations = "global::" + (string.IsNullOrEmpty(entity.Namespace) ? string.Empty : entity.Namespace + ".") + entity.EntityName + "RepositoryOperations";
            builder.Append("            global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<")
                .Append(operations).AppendLine(">(services);");
            var contracts = ContractNames(entity).Append("I" + entity.EntityName + "Repository")
                .Select(name => "global::" + model.Settings.ContractNamespace + "." + name)
                .Concat((entity.ReadPorts ?? []).Concat(entity.WritePorts ?? []).Select(static port => port.TypeName))
                .Distinct(StringComparer.Ordinal);
            foreach (var contract in contracts)
            {
                builder.Append("            global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<")
                    .Append(contract)
                    .Append(">(services, static provider => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<")
                    .Append(operations).AppendLine(">(provider));");
            }
        }

        builder.AppendLine("            return services;\n        }\n    }\n}");
    }
}
