namespace RonSijm.RepoGen.Testing;

internal sealed record InMemoryPreparedSave(int AffectedRows, Action Commit, IReadOnlyDictionary<object, object>? GeneratedRows);
