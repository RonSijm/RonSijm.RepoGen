using System.Text;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

/// <summary>Emits an ordered IQueryable pipeline; materialization happens only at its terminal.</summary>
internal static class ResultPipelineSourceRenderer
{
    private const string Queryable = "global::System.Linq.Queryable.";
    private const string Ef = "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.";

    public static void Append(StringBuilder builder, ConfiguredQueryModel query, string indent, bool directRoot = false, string tokenName = "cancellationToken")
    {
        void Line(string text) => builder.Append(indent).AppendLine(text);

        if (query.Source is not null && !directRoot)
        {
            Line("var __repoGenContext = selector.SelectorContext ?? throw new global::System.InvalidOperationException(\"This configured query requires a context-backed repository.\");");
        }

        var source = query.Source?.ExpressionSource ?? (query.Group is null
            ? Queryable + "Select(selector.Query, " + query.ProjectionExpressionSource + ")"
            : Queryable + "Select(" + Queryable + "GroupBy(selector.Query, " + query.Group.KeySelectorExpressionSource + "), " + query.Group.ProjectionExpressionSource + ")");
        Line("var __repoGenQuery0 = " + source + ";");
        var index = 0;
        var current = "__repoGenQuery0";
        foreach (var stage in query.Pipeline!.Stages)
        {
            var next = "__repoGenQuery" + ++index;
            if (stage.FieldSort is { } fieldSort)
            {
                Line("var " + next + " = " + current + ";");
                Line("switch (" + fieldSort.IndexName + ")");
                Line("{");
                for (var fieldIndex = 0; fieldIndex < fieldSort.Fields.Count; fieldIndex++)
                {
                    var key = fieldSort.Fields[fieldIndex].KeySelectorExpressionSource;
                    Line("    case " + fieldIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":");
                    Line("        " + next + " = " + fieldSort.DescendingName + " ? " + Queryable + "OrderByDescending(" + current + ", " + key + ") : " + Queryable + "OrderBy(" + current + ", " + key + ");");
                    Line("        break;");
                }

                Line("    case -1: break;");
                Line("    default: throw new global::System.ArgumentOutOfRangeException(\"sortField\");");
                Line("}");
                current = next;
                continue;
            }

            var expression = stage.ExpressionSource;
            var condition = stage.ConditionSource;
            if (stage.Kind is RepositoryQueryStageKind.Skip or RepositoryQueryStageKind.Take)
            {
                var count = "__repoGenCount" + index;
                if (condition is not null)
                {
                    var apply = "__repoGenApply" + index;
                    Line("var " + apply + " = (" + condition + ");");
                    condition = apply;
                }

                Line("var " + count + " = " + (condition is null ? expression : condition + " ? (" + expression + ") : 0") + ";");
                Line("if (" + count + " < 0) throw new global::System.ArgumentOutOfRangeException(\"count\", \"Skip/Take count must be non-negative.\");");
                expression = count;
            }

            var call = stage.Kind == RepositoryQueryStageKind.GroupBy
                ? Queryable + "Select(" + Queryable + "GroupBy(" + current + ", " + expression + "), " + stage.ProjectionExpressionSource + ")"
                : stage.Kind is RepositoryQueryStageKind.ThenBy or RepositoryQueryStageKind.ThenByDescending
                    ? "__repoGenThenBy(" + current + ", " + expression + ", " + (stage.Kind == RepositoryQueryStageKind.ThenByDescending ? "true" : "false") + ")"
                    : Queryable + (stage.Kind == RepositoryQueryStageKind.Membership ? "Where" : stage.Kind.ToString()) + "(" + current + (expression is null ? "" : ", " + expression) + ")";
            Line("var " + next + " = " + (condition is null ? call : "(" + condition + ") ? " + call + " : " + current) + ";");
            current = next;
        }

        if (query.ResultNullabilityConfigured)
        {
            // Annotation-only cast: same CLR row type, no extra SQL expression or client-side mapping.
            Line("var __repoGenAnnotatedResults = (global::System.Linq.IQueryable<" + query.ResultTypeName + ">)" + current + ";");
            current = "__repoGenAnnotatedResults";
        }

        switch (query.ResultKind)
        {
            case RepositoryQueryResultKind.List:
            case RepositoryQueryResultKind.GroupedList:
                Line("return await " + Ef + "ToListAsync(" + current + ", " + tokenName + ").ConfigureAwait(false);");
                break;
            case RepositoryQueryResultKind.Single:
            case RepositoryQueryResultKind.SingleOrDefault:
            case RepositoryQueryResultKind.FirstOrDefault:
                if (query.ResultKind != RepositoryQueryResultKind.Single && query.Pipeline.ResultIsNonNullableValueType)
                {
                    current = Queryable + "Select(" + current + ", static entity => (" + query.ResultTypeName + "?)entity)";
                }

                Line("return await " + Ef + query.ResultKind + "Async(" + current + ", " + tokenName + ").ConfigureAwait(false);");
                break;
            case RepositoryQueryResultKind.Exists:
                Line("return await " + Ef + "AnyAsync(" + current + ", " + tokenName + ").ConfigureAwait(false);");
                break;
            case RepositoryQueryResultKind.Count:
                Line("return await " + Ef + "CountAsync(" + current + ", " + tokenName + ").ConfigureAwait(false);");
                break;
            case RepositoryQueryResultKind.Max:
            case RepositoryQueryResultKind.Min:
            case RepositoryQueryResultKind.Sum:
            case RepositoryQueryResultKind.Average:
                Line("return await " + Ef + query.ResultKind + "Async(" + current + ", " + query.Scalar!.SelectorExpressionSource + ", " + tokenName + ").ConfigureAwait(false);");
                break;
            case RepositoryQueryResultKind.Dictionary:
            case RepositoryQueryResultKind.Lookup:
                // Only the key/value pair is selected in SQL. Dictionary assembly is a final materialization step.
                Line("var __repoGenPairs = await " + Ef + "ToListAsync(" + Queryable + "Select(" + current + ", entity => new { Key = " + query.Dictionary!.KeyBodySource + ", Value = " + query.Dictionary.ValueBodySource + " }), " + tokenName + ").ConfigureAwait(false);");
                if (query.ResultKind == RepositoryQueryResultKind.Lookup)
                {
                    Line("return global::System.Linq.Enumerable.ToDictionary(global::System.Linq.Enumerable.GroupBy(__repoGenPairs, static pair => pair.Key), static group => group.Key, static group => (global::System.Collections.Generic.IReadOnlyList<" + query.Dictionary.ValueTypeName + ">)global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(group, static pair => pair.Value)));");
                }
                else if (query.Dictionary.DuplicateKeys != RepositoryDuplicateKeyBehavior.Throw)
                {
                    Line("var __repoGenDictionary = new global::System.Collections.Generic.Dictionary<" + query.Dictionary.KeyTypeName + ", " + query.Dictionary.ValueTypeName + ">();");
                    Line(tokenName + ".ThrowIfCancellationRequested();");
                    Line("foreach (var __repoGenPair in __repoGenPairs)");
                    Line("{");
                    Line("    " + tokenName + ".ThrowIfCancellationRequested();");
                    Line(query.Dictionary.DuplicateKeys == RepositoryDuplicateKeyBehavior.KeepFirst
                        ? "    __repoGenDictionary.TryAdd(__repoGenPair.Key, __repoGenPair.Value);"
                        : "    __repoGenDictionary[__repoGenPair.Key] = __repoGenPair.Value;");
                    Line("}");
                    Line(tokenName + ".ThrowIfCancellationRequested();");
                    Line("return __repoGenDictionary;");
                }
                else
                {
                    Line("return global::System.Linq.Enumerable.ToDictionary(__repoGenPairs, static pair => pair.Key, static pair => pair.Value);");
                }

                break;
            case RepositoryQueryResultKind.CursorPaged:
                Line("global::System.ArgumentNullException.ThrowIfNull(cursorPage);");
                Line("global::System.Linq.IQueryable<" + query.ResultTypeName + "> __repoGenSeek = " + current + ";");
                Line("if (cursorPage.HasCursor)");
                Line("{");
                Line("    var cursor = cursorPage.Cursor!;");
                Line("    __repoGenSeek = " + Queryable + "Where(__repoGenSeek, entity => " + query.Cursor!.SeekPredicateBodySource + ");");
                Line("}");
                Line("var __repoGenCursorLimit = checked(cursorPage.PageSize + 1);");
                Line("var __repoGenCursorItems = await " + Ef + "ToListAsync(" + Queryable + "Take(__repoGenSeek, __repoGenCursorLimit), " + tokenName + ").ConfigureAwait(false);");
                Line("var __repoGenCursorMore = __repoGenCursorItems.Count > cursorPage.PageSize;");
                Line("if (__repoGenCursorMore) __repoGenCursorItems.RemoveAt(cursorPage.PageSize);");
                Line("global::System.Func<" + query.ResultTypeName + ", " + query.Cursor.CursorTypeName + "> __repoGenCursorSelector = " + query.Cursor.CursorSelectorExpressionSource + ";");
                Line(query.Cursor.CursorTypeName + " __repoGenNextCursor = __repoGenCursorItems.Count == 0 ? default! : __repoGenCursorSelector(__repoGenCursorItems[^1]);");
                Line("return new " + (directRoot ? "global::RonSijm.RepoGen.Abstractions." : query.InfrastructurePrefix) + "CursorPagedResult<" + query.ResultTypeName + ", " + query.Cursor.CursorTypeName + ">(__repoGenCursorItems, __repoGenCursorMore, __repoGenNextCursor" + (directRoot ? ", cursorPage.PageSize" : "") + ");");
                break;
            case RepositoryQueryResultKind.Paged:
            case RepositoryQueryResultKind.Slice:
                Line("global::System.ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);");
                Line("global::System.ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);");
                Line("var __repoGenOffset = checked((page - 1) * pageSize);");
                if (query.ResultKind == RepositoryQueryResultKind.Paged)
                {
                    Line("var __repoGenTotal = await " + Ef + "CountAsync(" + current + ", " + tokenName + ").ConfigureAwait(false);");
                    Line("var __repoGenItems = await " + Ef + "ToListAsync(" + Queryable + "Take(" + Queryable + "Skip(" + current + ", __repoGenOffset), pageSize), " + tokenName + ").ConfigureAwait(false);");
                    Line("return new " + (directRoot ? "global::RonSijm.RepoGen.Abstractions." : query.InfrastructurePrefix) + "PagedResult<" + query.ResultTypeName + ">(__repoGenItems, __repoGenTotal, page, pageSize);");
                }
                else
                {
                    Line("var __repoGenLimit = checked(pageSize + 1);");
                    Line("var __repoGenItems = await " + Ef + "ToListAsync(" + Queryable + "Take(" + Queryable + "Skip(" + current + ", __repoGenOffset), __repoGenLimit), " + tokenName + ").ConfigureAwait(false);");
                    Line("var __repoGenHasMore = __repoGenItems.Count > pageSize;");
                    Line("if (__repoGenHasMore) __repoGenItems.RemoveAt(pageSize);");
                    Line("return new global::RonSijm.RepoGen.Abstractions.SliceResult<" + query.ResultTypeName + ">(__repoGenItems, page, pageSize, __repoGenHasMore);");
                }

                break;
            case RepositoryQueryResultKind.Stream:
                Line("await foreach (var __repoGenItem in global::System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation(" + Ef + "AsAsyncEnumerable(" + current + "), " + tokenName + ").ConfigureAwait(false))");
                Line("{");
                Line("    " + tokenName + ".ThrowIfCancellationRequested();");
                Line("    yield return __repoGenItem;");
                Line("}");
                Line(tokenName + ".ThrowIfCancellationRequested();");
                break;
            default:
                throw new InvalidOperationException($"Unsupported result pipeline terminal '{query.ResultKind}'.");
        }
    }

    public static void AppendOrderingHelper(StringBuilder builder, string indent, bool fieldSorting = false)
    {
        // Inference avoids naming an anonymous intermediate row; ordering chains are validated as adjacent.
        builder.Append(indent).AppendLine("private static global::System.Linq.IOrderedQueryable<T> __repoGenThenBy<T, TKey>(");
        builder.Append(indent).Append("    global::System.Linq.").Append(fieldSorting ? "IQueryable" : "IOrderedQueryable").AppendLine("<T> source, global::System.Linq.Expressions.Expression<global::System.Func<T, TKey>> key, bool descending)");
        builder.Append(indent).AppendLine("{");
        // A whitelisted switch has a common IQueryable type, even for anonymous rows. The designer
        // proves every branch reaching ThenBy is ordered; no runtime reflection/type construction is needed.
        var source = fieldSorting ? "(global::System.Linq.IOrderedQueryable<T>)source" : "source";
        builder.Append(indent).Append("    return descending ? global::System.Linq.Queryable.ThenByDescending(").Append(source).AppendLine(", key)");
        builder.Append(indent).Append("        : global::System.Linq.Queryable.ThenBy(").Append(source).AppendLine(", key);");
        builder.Append(indent).AppendLine("}");
    }
}