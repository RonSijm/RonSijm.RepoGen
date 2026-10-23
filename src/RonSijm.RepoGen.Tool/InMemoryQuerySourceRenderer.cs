using System.Text;
using System.Text.Json;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Lowers validated read definitions to typed delegates over detached fixture snapshots.</summary>
internal static class InMemoryQuerySourceRenderer
{
    private const string Linq = "global::System.Linq.Enumerable.";
    private const string Runtime = "global::RonSijm.RepoGen.Testing.InMemoryQueryExecution.";

    internal static ConfiguredQueryModel? Query(EntityRepositoryModel entity, ScriptedTestDoubleOperationModel operation)
        => entity.Queries.SingleOrDefault(query => RepositoryOperationSignature.Query(query) == operation.Method.Signature);

    internal static string HandlerName(ScriptedTestDoubleOperationModel operation) => "__repoGenExecute" + operation.ScriptName;

    internal static (string Status, string Reason) Coverage(EntityRepositoryModel entity, ScriptedTestDoubleOperationModel operation)
    {
        var query = Query(entity, operation);
        var write = InMemoryCommandSourceRenderer.Command(entity, operation);
        if (write?.InMemoryCommand is { } command)
            return command.Supported ? ("Generated", command.SavedResult is not null && write.SavedResultMaximumRows is not null
                ? "Explicit bounded fresh native-update results over a detached prepared root overlay and saved-only declared relationships before fixture publication; complete-key correlation, retained staging and no provider guarantees. Scripts override generated behavior."
                : command.SavedResult is not null && write.IsAtomic
                ? "Explicit atomic saved-result query over a detached prepared root overlay and saved-only declared relationships before fixture publication; pending writes remain, without native graph buffers or provider guarantees. Scripts override generated behavior."
                : command.ReturningState is not null && write.IsAtomic
                ? "Explicit atomic scalar fixture capture over complete write state; saved-only insert/update factories and results finish before commit, without native buffers, tracker entries or SQL/race guarantees. Scripts override generated behavior."
                : command.ReturningState is not null
                ? "Explicit mapped scalar returned-value fixture simulation over complete write state; post-update/pre-delete values are prepared before commit, not provider buffers or tracker state. Scripts override generated behavior."
                : command.HasMappedState && command.Collections is { Count: > 0 }
                ? "Explicit mapped scalar parent/child state in bounded owner-scoped reconciliation; complete round trips, original versions and factories retain Stage/shared Save boundaries without graph or provider inference. Scripts override generated behavior."
                : command.MappedPropertyState is { Count: > 0 }
                ? "Explicit typed mapped scalar fixture state with full round-trip validation and explicit expected-version/factory semantics; ordinary identity, graph and provider boundaries remain. Scripts override generated behavior."
                : command.Translations is { Count: > 0 }
                ? "Explicit per-command scalar-call fixture translations; designed write/Stage/Save/result semantics remain, without provider equivalence. Scripts override generated behavior."
                : command.SavedResult?.ModelQueryFilters is { Count: > 0 }
                ? "Explicit scalar command selection and filtered ordinary saved DTO graphs after shared Save; related graph permissions and local null guards remain, without graph mutations or provider equivalence. Scripts override generated behavior."
                : command.IsImmediate && write.IsReturning && !write.IsAtomic && command.ModelQueryFilter is not null
                ? "Explicit model-filtered saved selection for scalar native-returning simulation; updated/deleted values are not fresh filtered reads, pending state is retained, and provider equivalence is not inferred. Scripts override generated behavior."
                : command.IsImmediate && write.IsAtomic && command.ModelQueryFilter is not null
                ? "Explicit scalar atomic fixture simulation with unfiltered conflicts/insertion, filtered update selection and model-only updated result reads; pending state remains, without graph or provider guarantees. Scripts override generated behavior."
                : command.ModelQueryFilter is not null
                ? "Explicit per-command final-model scalar filter on saved selection/fresh result reads, not insertion/shared Save; graph/native/provider semantics remain separate. Scripts override generated behavior."
                : command.IsImmediate && write.IsAtomic
                ? "Explicit saved-state insert/upsert fixture simulation; declared conflict target, update-only visibility, pending writes retained, no SQL constraints or race guarantees."
                : command.IsImmediate
                ? "Explicit immediate scalar fixture simulation; saved rows only, pending writes retained, no relational race guarantees."
                : command.SavedResult is not null
                ? "Explicit saved-state result query after shared save; detached declared relationships, projection-owned related visibility, no atomic returning/provider guarantees."
                : command.Collections is { Count: > 0 } && (command.ModelQueryFilter is not null || command.Collections.Any(static collection => collection.State.ModelQueryFilter is not null))
                ? "Explicit owner-scoped model-filtered child/link reconciliation; independent write consent, unfiltered insertion/shared Save and dirty-target checks, no provider guarantees. Scripts override generated behavior."
                : command.Collections is { Count: > 0 }
                ? "Explicit bounded child-store reconciliation; mapped ownership, scalar writes, shared Stage/Save, no entity-graph or provider guarantees."
                : "Portable staged/shared-save fixture write; scripts override generated behavior.") : ("RequiresOverride", string.Join(" ", command.Reasons));
        return query?.InMemoryQuery is { Supported: true }
            ? ("Generated", query.InMemoryQuery.Translations is { Count: > 0 }
                ? "Designer-declared expression translations over detached fixture snapshots; test semantics are explicit, not provider equivalence. Scripts override generated behavior."
                : query.InMemoryQuery.PropertyReads is { Count: > 0 }
                ? "Explicit typed mapped scalar values from public fixture data on detached read rows; filter/graph permissions remain independent, without tracked state or provider equivalence. Scripts override generated behavior."
                : query.InMemoryQuery.Navigation?.Roots.Any(static root => root.ModelQueryFilter is not null) == true
                ? "Explicit scalar model filters before declared collection/optional-reference loading on detached snapshots; required filtered references, many-to-many graphs and write/provider semantics remain separate. Scripts override generated behavior."
                : query.InMemoryQuery.ModelQueryFilters is { Count: > 0 }
                ? "Explicit final-model scalar filters on detached read roots before designer visibility/pipelines; context/navigation/write/provider semantics are not inferred. Scripts override generated behavior."
                : "Portable detached fixture read; explicit scripts override generated behavior.")
            : ("RequiresOverride", query?.InMemoryQuery is { } coverage ? string.Join(" ", coverage.Reasons) : "Write/persistence behavior is not simulated; arrange a typed test script.");
    }

    internal static void AppendHandler(StringBuilder builder, RepositoryGenerationModel model, EntityRepositoryModel entity,
        ScriptedTestDoubleOperationModel operation)
    {
        builder.Append("        private ").Append(operation.Method.ReturnType.StartsWith("global::System.Threading.Tasks.Task<", StringComparison.Ordinal)
                ? operation.Method.ReturnType : "global::System.Threading.Tasks.Task<" + operation.Method.ReturnType + ">")
            .Append(' ').Append(HandlerName(operation)).Append('(').Append(operation.ArgumentsName)
            .AppendLine(" __repoGenArguments, global::System.Threading.CancellationToken cancellationToken)\n        {");
        void Line(string line) => builder.Append("            ").AppendLine(line);
        Line("cancellationToken.ThrowIfCancellationRequested();");
        var query = Query(entity, operation);
        if (query?.InMemoryQuery is not { Supported: true })
        {
            Line("throw new global::RonSijm.RepoGen.Testing.UnsupportedInMemoryRepositoryOperationException(" +
                 JsonSerializer.Serialize(model.ContextTypeName + ":" + entity.EntityName + "." + operation.Method.Signature) + ", " +
                 JsonSerializer.Serialize(Coverage(entity, operation).Reason) + ");");
            builder.AppendLine("        }");
            return;
        }
        var execution = query.InMemoryQuery;
        if (execution.Rendering is { } rendering)
        {
            query = rendering.Query;
            entity = entity with { DefaultSorts = rendering.DefaultSorts, VisibilityPolicies = rendering.VisibilityPolicies };
        }

        var parameters = ProjectionSourceRenderer.CreateQueryParameters(query);
        var properties = ScriptedTestDoubleSourceRenderer.ArgumentPropertyNames(operation);
        for (var index = 0; index < parameters.Count; index++)
            Line("var " + parameters[index].Name + " = __repoGenArguments." + properties[index] + ";");
        ClosedRepositorySourceRenderer.AppendValidations(builder, query.Validations);
        RepositoryQueryPreparationSourceRenderer.AppendPreparation(builder, query, "            ");
        if (query.ResultKind is RepositoryQueryResultKind.Paged or RepositoryQueryResultKind.Slice)
        {
            Line("global::System.ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);");
            Line("global::System.ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);");
            Line("var __repoGenOffset = checked((page - 1) * pageSize);");
            if (query.ResultKind == RepositoryQueryResultKind.Slice) Line("var __repoGenLimit = checked(pageSize + 1);");
        }
        if (query.ResultKind == RepositoryQueryResultKind.CursorPaged)
        {
            Line("global::System.ArgumentNullException.ThrowIfNull(cursorPage);");
            Line("var __repoGenCursorLimit = checked(cursorPage.PageSize + 1);");
        }
        if (query.DynamicSort is { } dynamicSort)
        {
            Line("if (" + string.Join(" && ", dynamicSort.Fields.Select(field => "!global::System.String.Equals(" +
                dynamicSort.FieldNameParameter + ", " + JsonSerializer.Serialize(field.PropertyName) + ", global::System.StringComparison.OrdinalIgnoreCase)")) + ")");
            Line("    throw new global::System.ArgumentOutOfRangeException(nameof(" + dynamicSort.FieldNameParameter + "), \"The sort field is not in this operation's whitelist.\");");
        }
        Line("cancellationToken.ThrowIfCancellationRequested();");
        var boundFilters = execution.ModelQueryFilters?.Any(static filter => filter.ContextBindings is { Count: > 0 }) == true;
        if (boundFilters) Line("var " + InMemoryModelFilterContextExpressions.KeyVariable + " = " + query.ContextKeySource + ";");
        var data = model.Settings.ContextKeyTypeName is null ? "Fixture.Data" : "Fixture.For(" +
            (boundFilters ? InMemoryModelFilterContextExpressions.KeyVariable : query.ContextKeySource) + ")";
        var navigation = execution.Navigation;
        if (navigation is not null) InMemoryNavigationSourceRenderer.AppendSnapshot(Line, data, navigation);
        if (execution.Source is { } source)
        {
            if (navigation is null) Line("var __repoGenSnapshot = " + data + ".CaptureSnapshot(" + string.Join(", ", source.Roots.Select(root => JsonSerializer.Serialize(root.EntityTypeName))) + ");");
            foreach (var sourceRoot in source.Roots)
            {
                var rows = Runtime + "WithCancellation(" + (navigation is null
                    ? "__repoGenSnapshot.GetRows<" + (sourceRoot.StoreRowTypeName ?? sourceRoot.EntityTypeName) + ">(" + JsonSerializer.Serialize(sourceRoot.EntityTypeName) + ")"
                    : InMemoryNavigationSourceRenderer.Rows(navigation, sourceRoot.EntityTypeName)) + ", cancellationToken)";
                if (navigation is null && sourceRoot.EntityMapping is { } mapping) rows = Linq + "Select(" + rows + ", " + mapping.ExpressionSource + ")";
                Line("global::System.Collections.Generic.IEnumerable<" + (sourceRoot.EntityMapping?.ReadTypeName ?? sourceRoot.EntityTypeName) + "> " + sourceRoot.VariableName + " = " + rows + ";");
                foreach (var filter in execution.ModelQueryFilters?.Where(filter => filter.EntityTypeName == sourceRoot.EntityTypeName && navigation is null) ?? [])
                    Line(sourceRoot.VariableName + " = " + Linq + "Where(" + sourceRoot.VariableName + ", " + filter.PredicateSource + ");");
                foreach (var policy in sourceRoot.VisibilityPredicates)
                    Line(sourceRoot.VariableName + " = " + Linq + "Where(" + sourceRoot.VariableName + ", " + policy + ");");
            }
            Line("var __repoGenQuery0 = " + source.ExpressionSource + ";");
            var results = AppendStages(Line, query);
            if (query.ResultNullabilityConfigured)
            {
                Line("var __repoGenAnnotatedResults = (global::System.Collections.Generic.IEnumerable<" + query.ResultTypeName + ">)" + results + ";");
                results = "__repoGenAnnotatedResults";
            }
            AppendTerminal(Line, query, operation, results);
            builder.AppendLine("        }");
            return;
        }
        var rootRows = Runtime + "WithCancellation(" + (navigation is null ? data + "." + entity.EntityName + "Store.Snapshot()"
            : InMemoryNavigationSourceRenderer.Rows(navigation, entity.EntityTypeName)) + ", cancellationToken)";
        if (navigation is null && entity.InMemoryStore?.EntityMapping is { } entityMapping) rootRows = Linq + "Select(" + rootRows + ", " + entityMapping.ExpressionSource + ")";
        Line("global::System.Collections.Generic.IEnumerable<" + (entity.InMemoryStore?.EntityMapping?.ReadTypeName ?? entity.EntityTypeName) + "> __repoGenRoot = " + rootRows + ";");
        foreach (var filter in execution.ModelQueryFilters?.Where(_ => navigation is null) ?? [])
            Line("__repoGenRoot = " + Linq + "Where(__repoGenRoot, " + filter.PredicateSource + ");");
        foreach (var policy in (entity.VisibilityPolicies ?? []).Where(policy => query.BypassedVisibility?.Contains(policy.Name, StringComparer.Ordinal) != true))
            Line("__repoGenRoot = " + Linq + "Where(__repoGenRoot, entity => " + policy.PredicateSource + ");");
        AppendSorts(Line, query.UseDefaultSort ? entity.DefaultSorts : []);
        foreach (var predicate in query.Predicates)
        {
            if (predicate.ConditionSource is not null) Line("if (" + predicate.ConditionSource + ")");
            Line((predicate.ConditionSource is null ? "" : "    ") + "__repoGenRoot = " + Linq + "Where(__repoGenRoot, entity => " + predicate.PredicateSource + ");");
        }
        AppendSorts(Line, query.Sorts);
        if (query.DynamicSort is { } sorting)
        {
            for (var index = 0; index < sorting.Fields.Count; index++)
            {
                var field = sorting.Fields[index].PropertyName;
                var member = entity.InMemoryStore?.EntityMapping?.PropertyMappings?.SingleOrDefault(property => property.PropertyName == field)?.MemberName ?? field;
                var key = "entity => entity." + RepositoryModelFactory.EscapeIdentifier(member);
                Line((index == 0 ? "if" : "else if") + " (global::System.String.Equals(" + sorting.FieldNameParameter + ", " + JsonSerializer.Serialize(field) + ", global::System.StringComparison.OrdinalIgnoreCase))");
                Line("    __repoGenRoot = " + sorting.DescendingParameter + " ? " + Linq + "OrderByDescending(__repoGenRoot, " + key + ") : " + Linq + "OrderBy(__repoGenRoot, " + key + ");");
            }
        }
        if (query.ResultKind == RepositoryQueryResultKind.CursorPaged && query.Cursor?.OnResults != true)
        {
            Line("if (cursorPage.HasCursor)");
            Line("{");
            Line("    var cursor = cursorPage.Cursor!;");
            Line("    __repoGenRoot = " + Linq + "Where(__repoGenRoot, entity => " + query.Cursor!.SeekPredicateBodySource + ");");
            Line("}");
            if (query.Pipeline is null)
            {
                AppendEntityCursor(Line, query, operation);
                builder.AppendLine("        }");
                return;
            }
        }

        var root = query.Pipeline is null && query.ResultKind is RepositoryQueryResultKind.Count or RepositoryQueryResultKind.Exists
            ? "__repoGenRoot"
            : query.Group is null
                ? Linq + "Select(__repoGenRoot, " + query.ProjectionExpressionSource + ")"
                : Linq + "Select(" + Linq + "GroupBy(__repoGenRoot, " + query.Group.KeySelectorExpressionSource + "), " + query.Group.ProjectionExpressionSource + ")";
        Line("var __repoGenQuery0 = " + root + ";");
        var current = AppendStages(Line, query);
        if (query.ResultNullabilityConfigured && query.Pipeline is not null)
        {
            Line("var __repoGenAnnotatedResults = (global::System.Collections.Generic.IEnumerable<" + query.ResultTypeName + ">)" + current + ";");
            current = "__repoGenAnnotatedResults";
        }
        AppendTerminal(Line, query, operation, current);
        builder.AppendLine("        }");
    }

    private static void AppendSorts(Action<string> line, IReadOnlyList<SortModel> sorts)
    {
        for (var index = 0; index < sorts.Count; index++)
        {
            var sort = sorts[index];
            line("__repoGenRoot = " + (index == 0
                ? Linq + (sort.IsDescending ? "OrderByDescending" : "OrderBy") + "(__repoGenRoot, " + sort.ExpressionSource + ")"
                : "__repoGenThenBy(__repoGenRoot, " + sort.ExpressionSource + ", " + (sort.IsDescending ? "true" : "false") + ")") + ";");
        }
    }

    private static string AppendStages(Action<string> line, ConfiguredQueryModel query)
    {
        var current = "__repoGenQuery0";
        var index = 0;
        foreach (var stage in query.Pipeline?.Stages ?? [])
        {
            var next = "__repoGenQuery" + ++index;
            if (stage.FieldSort is { } sort)
            {
                line("var " + next + " = " + Linq + "AsEnumerable(" + current + ");");
                line("switch (" + sort.IndexName + ")");
                line("{");
                for (var fieldIndex = 0; fieldIndex < sort.Fields.Count; fieldIndex++)
                {
                    var key = sort.Fields[fieldIndex].KeySelectorExpressionSource;
                    line("    case " + fieldIndex + ":");
                    line("        " + next + " = " + sort.DescendingName + " ? " + Linq + "OrderByDescending(" + current + ", " + key + ") : " + Linq + "OrderBy(" + current + ", " + key + ");");
                    line("        break;");
                }
                line("    case -1: break;");
                line("    default: throw new global::System.ArgumentOutOfRangeException(\"sortField\");");
                line("}");
                current = next;
                continue;
            }
            var expression = stage.ExpressionSource;
            var condition = stage.ConditionSource;
            if (stage.Kind is RepositoryQueryStageKind.Skip or RepositoryQueryStageKind.Take)
            {
                if (condition is not null)
                {
                    line("var __repoGenApply" + index + " = (" + condition + ");");
                    condition = "__repoGenApply" + index;
                }
                line("var __repoGenCount" + index + " = " + (condition is null ? expression : condition + " ? (" + expression + ") : 0") + ";");
                line("if (__repoGenCount" + index + " < 0) throw new global::System.ArgumentOutOfRangeException(\"count\", \"Skip/Take count must be non-negative.\");");
                expression = "__repoGenCount" + index;
            }
            var call = stage.Kind == RepositoryQueryStageKind.GroupBy
                ? Linq + "Select(" + Linq + "GroupBy(" + current + ", " + expression + "), " + stage.ProjectionExpressionSource + ")"
                : stage.Kind is RepositoryQueryStageKind.ThenBy or RepositoryQueryStageKind.ThenByDescending
                    ? "__repoGenThenBy(" + current + ", " + expression + ", " + (stage.Kind == RepositoryQueryStageKind.ThenByDescending ? "true" : "false") + ")"
                    : Linq + (stage.Kind == RepositoryQueryStageKind.Membership ? "Where" : stage.Kind.ToString()) + "(" + current + (expression is null ? "" : ", " + expression) + ")";
            line("var " + next + " = " + (condition is null ? call : "(" + condition + ") ? " + call + " : " + Linq + "AsEnumerable(" + current + ")") + ";");
            current = next;
        }
        return current;
    }

    private static void AppendTerminal(Action<string> line, ConfiguredQueryModel query, ScriptedTestDoubleOperationModel operation, string current)
    {
        void Return(string result) => line("cancellationToken.ThrowIfCancellationRequested();\n            return global::System.Threading.Tasks.Task.FromResult<" +
            (operation.Stream ? operation.Method.ReturnType : operation.ResultType) + ">(" + result + ");");
        switch (query.ResultKind)
        {
            case RepositoryQueryResultKind.List:
            case RepositoryQueryResultKind.GroupedList:
                line("var __repoGenResult = " + Linq + "ToList(" + current + ");");
                Return("__repoGenResult"); break;
            case RepositoryQueryResultKind.Single:
            case RepositoryQueryResultKind.SingleOrDefault:
            case RepositoryQueryResultKind.FirstOrDefault:
                if (query.ResultKind != RepositoryQueryResultKind.Single && query.Pipeline?.ResultIsNonNullableValueType == true)
                    current = Linq + "Select(" + current + ", static entity => (" + query.ResultTypeName + "?)entity)";
                line("var __repoGenResult = " + Linq + query.ResultKind + "(" + current + ");");
                Return("__repoGenResult"); break;
            case RepositoryQueryResultKind.Count:
            case RepositoryQueryResultKind.Exists:
                line("var __repoGenResult = " + Linq + (query.ResultKind == RepositoryQueryResultKind.Count ? "Count" : "Any") + "(" + current + ");");
                Return("__repoGenResult"); break;
            case RepositoryQueryResultKind.Min:
            case RepositoryQueryResultKind.Max:
            case RepositoryQueryResultKind.Sum:
            case RepositoryQueryResultKind.Average:
                line("var __repoGenResult = " + Linq + query.ResultKind + "(" + current + ", " + query.Scalar!.SelectorExpressionSource + ");");
                Return("__repoGenResult"); break;
            case RepositoryQueryResultKind.Dictionary:
            case RepositoryQueryResultKind.Lookup:
                line("var __repoGenPairs = " + Linq + "ToList(" + Linq + "Select(" + current + ", entity => new { Key = " + query.Dictionary!.KeyBodySource + ", Value = " + query.Dictionary.ValueBodySource + " }));");
                if (query.ResultKind == RepositoryQueryResultKind.Lookup)
                {
                    line("var __repoGenResult = " + Linq + "ToDictionary(" + Linq + "GroupBy(__repoGenPairs, static pair => pair.Key), static group => group.Key, static group => (global::System.Collections.Generic.IReadOnlyList<" + query.Dictionary.ValueTypeName + ">)" + Linq + "ToList(" + Linq + "Select(group, static pair => pair.Value)));");
                }
                else
                {
                    line("var __repoGenResult = new global::System.Collections.Generic.Dictionary<" + query.Dictionary.KeyTypeName + ", " + query.Dictionary.ValueTypeName + ">();");
                    line("foreach (var __repoGenPair in __repoGenPairs)");
                    line("{");
                    line("    cancellationToken.ThrowIfCancellationRequested();");
                    line(query.Dictionary.DuplicateKeys switch
                    {
                        RepositoryDuplicateKeyBehavior.KeepFirst => "    __repoGenResult.TryAdd(__repoGenPair.Key, __repoGenPair.Value);",
                        RepositoryDuplicateKeyBehavior.KeepLast => "    __repoGenResult[__repoGenPair.Key] = __repoGenPair.Value;",
                        _ => "    __repoGenResult.Add(__repoGenPair.Key, __repoGenPair.Value);"
                    });
                    line("}");
                }
                Return("__repoGenResult"); break;
            case RepositoryQueryResultKind.Paged:
            case RepositoryQueryResultKind.Slice:
                // Materialize once: count and items are from the same detached snapshot and transformed sequence.
                if (query.ResultKind == RepositoryQueryResultKind.Paged)
                {
                    line("var __repoGenResults = " + Linq + "ToList(" + current + ");");
                    line("var __repoGenItems = " + Linq + "ToList(" + Linq + "Take(" + Linq + "Skip(__repoGenResults, __repoGenOffset), pageSize));");
                    Return("new global::RonSijm.RepoGen.Abstractions.PagedResult<" + query.ResultTypeName + ">(__repoGenItems, __repoGenResults.Count, page, pageSize)");
                }
                else
                {
                    line("var __repoGenItems = " + Linq + "ToList(" + Linq + "Take(" + Linq + "Skip(" + current + ", __repoGenOffset), __repoGenLimit));");
                    line("var __repoGenMore = __repoGenItems.Count > pageSize;");
                    line("if (__repoGenMore) __repoGenItems.RemoveAt(pageSize);");
                    Return("new global::RonSijm.RepoGen.Abstractions.SliceResult<" + query.ResultTypeName + ">(__repoGenItems, page, pageSize, __repoGenMore)");
                }
                break;
            case RepositoryQueryResultKind.CursorPaged:
                line("var __repoGenSeek = " + Linq + "AsEnumerable(" + current + ");");
                if (query.Cursor!.OnResults)
                {
                    line("if (cursorPage.HasCursor)");
                    line("{");
                    line("    var cursor = cursorPage.Cursor!;");
                    line("    __repoGenSeek = " + Linq + "Where(__repoGenSeek, entity => " + query.Cursor.SeekPredicateBodySource + ");");
                    line("}");
                }
                line("var __repoGenItems = " + Linq + "ToList(" + Linq + "Take(__repoGenSeek, __repoGenCursorLimit));");
                line("var __repoGenMore = __repoGenItems.Count > cursorPage.PageSize;");
                line("if (__repoGenMore) __repoGenItems.RemoveAt(cursorPage.PageSize);");
                line("global::System.Func<" + query.ResultTypeName + ", " + query.Cursor.CursorTypeName + "> __repoGenCursorSelector = " + query.Cursor.CursorSelectorExpressionSource + ";");
                line(query.Cursor.CursorTypeName + " __repoGenNextCursor = __repoGenItems.Count == 0 ? default! : __repoGenCursorSelector(__repoGenItems[^1]);");
                Return("new global::RonSijm.RepoGen.Abstractions.CursorPagedResult<" + query.ResultTypeName + ", " + query.Cursor.CursorTypeName + ">(__repoGenItems, __repoGenMore, __repoGenNextCursor, cursorPage.PageSize)");
                break;
            case RepositoryQueryResultKind.Stream:
                Return(Runtime + "Stream(" + current + ", cancellationToken)"); break;
            default: throw new InvalidOperationException("Unsupported in-memory terminal: " + query.ResultKind);
        }
    }

    private static void AppendEntityCursor(Action<string> line, ConfiguredQueryModel query, ScriptedTestDoubleOperationModel operation)
    {
        line("var __repoGenRows = " + Linq + "ToList(" + Linq + "Take(__repoGenRoot, __repoGenCursorLimit));");
        line("var __repoGenMore = __repoGenRows.Count > cursorPage.PageSize;");
        line("if (__repoGenMore) __repoGenRows.RemoveAt(cursorPage.PageSize);");
        // Entity cursor expressions stay entity-bound even when the public result is a DTO/scalar.
        line(query.Cursor!.CursorTypeName + " __repoGenNextCursor = __repoGenRows.Count == 0 ? default! : " +
             Linq + "Last(" + Linq + "Select(__repoGenRows, " + query.Cursor.CursorSelectorExpressionSource + "));");
        line("var __repoGenItems = " + Linq + "ToList(" + Linq + "Select(__repoGenRows, " + query.ProjectionExpressionSource + "));");
        line("cancellationToken.ThrowIfCancellationRequested();");
        line("return global::System.Threading.Tasks.Task.FromResult<" + operation.ResultType + ">(new global::RonSijm.RepoGen.Abstractions.CursorPagedResult<" +
             query.ResultTypeName + ", " + query.Cursor.CursorTypeName + ">(__repoGenItems, __repoGenMore, __repoGenNextCursor, cursorPage.PageSize));");
    }

    internal static void AppendOrderingHelper(StringBuilder builder)
        => builder.AppendLine("        private static global::System.Linq.IOrderedEnumerable<T> __repoGenThenBy<T, TKey>(global::System.Collections.Generic.IEnumerable<T> source, global::System.Func<T, TKey> key, bool descending)\n        {\n            var ordered = (global::System.Linq.IOrderedEnumerable<T>)source;\n            return descending ? global::System.Linq.Enumerable.ThenByDescending(ordered, key) : global::System.Linq.Enumerable.ThenBy(ordered, key);\n        }");
}
