namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryQuerySourceModel(string ExpressionSource, IReadOnlyList<InMemoryQueryRootModel> Roots,
    IReadOnlyList<string> Operators);
