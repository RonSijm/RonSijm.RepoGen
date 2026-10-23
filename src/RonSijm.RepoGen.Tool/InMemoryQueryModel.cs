namespace RonSijm.RepoGen.Tool;

/// <summary>Test-only capability receipt; excluded from production query/cache shape serialization.</summary>
internal sealed record InMemoryQueryModel(bool Supported, IReadOnlyList<string> Reasons, InMemoryQuerySourceModel? Source = null,
    InMemoryNavigationModel? Navigation = null, IReadOnlyList<InMemoryTranslationReceiptModel>? Translations = null,
    InMemoryQueryRenderingModel? Rendering = null,
    IReadOnlyList<InMemoryModelQueryFilterModel>? ModelQueryFilters = null,
    IReadOnlyList<InMemoryPropertyReadModel>? PropertyReads = null);
