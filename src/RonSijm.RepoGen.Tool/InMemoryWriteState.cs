using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

/// <summary>Exact scalar slots for explicitly opted-in fixture state, never key or graph inference.</summary>
internal static class InMemoryWriteState
{
    internal static string Member(string propertyName, IReadOnlyList<InMemoryPropertyModel>? bindings)
        => bindings?.SingleOrDefault(property => property.PropertyName == propertyName)?.MemberName ?? propertyName;

    internal static string Access(string propertyName, IReadOnlyList<InMemoryPropertyModel>? bindings)
        => RepositoryModelFactory.EscapeIdentifier(Member(propertyName, bindings));

    internal static bool IsIdentity(IProperty property)
        => property.IsKey() || property.IsForeignKey() || property.GetContainingIndexes().Any(static index => index.IsUnique);

    internal static InMemoryTranslationExpressions? Translator(IEntityType entity, IReadOnlyList<InMemoryPropertyModel>? bindings)
        => bindings is { Count: > 0 } ? new([], entity.Model, propertyBindings: new(entity, bindings)) : null;
}
