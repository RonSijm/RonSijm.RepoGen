using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    /// <summary>Only the root and explicitly reconciled writable children grant scalar write bindings.</summary>
    private static InMemoryPropertyExpressions? CreateInMemoryCommandPropertyBindings(IEntityType root,
        IReadOnlyList<InMemoryPropertyModel>? rootState, RepositoryCommandDesign command,
        RepositoryGenerationSettingsModel settings, RepositoryDesign? design)
    {
        var participants = new Dictionary<IEntityType, IReadOnlyList<InMemoryPropertyModel>>();
        if (rootState is { Count: > 0 })
        {
            participants.Add(root, rootState);
        }
        if (command.InMemoryCollectionWrites && design is not null)
        {
            foreach (var collection in command.Collections)
            {
                var child = root.FindNavigation(((MemberExpression)collection.Navigation.Body).Member.Name)!.TargetEntityType;
                var definition = design.Entities.SingleOrDefault(entity => entity.EntityType == child.ClrType);
                if (participants.ContainsKey(child) || definition is not { RepositoryEnabled: true, WriteEnabled: true } ||
                    definition.TestDoubleEnabled == false || child.ClrType.IsNested || child.ClrType.IsGenericType ||
                    definition.InMemoryStore is not { WriteMapping: not null, MappedPropertyStateEnabled: true })
                {
                    continue;
                }
                var read = CreateInMemoryEntityMapping(child, definition, settings, design);
                var write = CreateInMemoryWriteMapping(child, definition, settings, read);
                if (write?.MappedPropertyState is { Count: > 0 } state)
                {
                    participants.Add(child, state);
                }
            }
        }
        return participants.Count == 0 ? null : new(participants.Select(static pair => (pair.Key, pair.Value)));
    }
}
