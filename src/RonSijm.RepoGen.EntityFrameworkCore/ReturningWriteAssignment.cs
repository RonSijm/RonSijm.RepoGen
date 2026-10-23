namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>An approved mapped property and a generated source-column alias; neither is caller SQL.</summary>
public sealed record ReturningWriteAssignment(string PropertyName, string ValueAlias);
