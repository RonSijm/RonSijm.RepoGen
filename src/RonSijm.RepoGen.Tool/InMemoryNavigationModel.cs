namespace RonSijm.RepoGen.Tool;

/// <summary>Test-only loading of explicitly declared edges over a coordinated detached snapshot.</summary>
internal sealed record InMemoryNavigationModel(IReadOnlyList<InMemoryNavigationRootModel> Roots,
    IReadOnlyList<InMemoryNavigationEdgeModel> Relationships);
