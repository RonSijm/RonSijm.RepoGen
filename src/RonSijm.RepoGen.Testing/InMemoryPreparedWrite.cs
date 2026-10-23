namespace RonSijm.RepoGen.Testing;

/// <summary>A fully encoded write and an independently detached view of its unpublished rows.</summary>
internal sealed record InMemoryPreparedWrite(Action Commit, (Type RowType, object Rows) Snapshot);
