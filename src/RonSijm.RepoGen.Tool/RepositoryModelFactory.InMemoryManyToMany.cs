using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryNavigationEdgeModel? CreateInMemoryManyToManyEdge(ISkipNavigation navigation, string name,
        Dictionary<IEntityType, string> variables, Dictionary<IEntityType, InMemoryEntityMappingModel?> mappings,
        IModel? relationshipMetadata, SortedSet<string> reasons, string assignment,
        IReadOnlyList<Expression> expressions, bool preserveMissingTargets, RepositoryDesign? design)
    {
        var join = navigation.JoinEntityType;
        var from = navigation.ForeignKey;
        var to = navigation.Inverse?.ForeignKey;
        if (to is null || from.DeclaringEntityType != join || to.DeclaringEntityType != join ||
            from.PrincipalEntityType != navigation.DeclaringEntityType || to.PrincipalEntityType != navigation.TargetEntityType)
        {
            reasons.Add("Navigation '" + name + "' needs an explicit many-to-many join with both mapped principal foreign keys and an inverse metadata edge; unsupported join direction requires a script.");
            return null;
        }
        if (!to.IsRequired)
        {
            var supported = true;
            if (preserveMissingTargets)
            {
                var usage = new InMemoryNullSafeJoinUsageInspector(navigation);
                foreach (var expression in expressions) usage.Visit(expression);
                supported = usage.Supported;
            }
            else
            {
                var usage = new InMemoryOptionalJoinUsageInspector(navigation);
                foreach (var expression in expressions) usage.Visit(expression);
                supported = usage.Supported;
            }
            if (!supported)
            {
                reasons.Add(preserveMissingTargets
                    ? "Navigation '" + name + "' preserves missing optional targets. Use supported scalar/DTO pipelines with explicit null-safe target access; unsafe dereferences, identity escape and Include/load-profile paths require a script."
                    : "Navigation '" + name + "' has an optional target join foreign key. Every use must start with Where(target => target != null), before sorting/projection/count/membership; Include/load-profile paths and unguarded SQL null propagation require a script.");
                return null;
            }
        }
        var comparers = new Dictionary<IProperty, InMemoryRelationshipComparerModel>();
        var sourceComparison = InMemoryRelationshipComparison(from, design, comparers);
        var targetComparison = InMemoryRelationshipComparison(to, design, comparers);
        if (!ValidateInMemoryNavigationKeys(name,
            [(navigation.DeclaringEntityType, from.PrincipalKey.Properties), (navigation.TargetEntityType, to.PrincipalKey.Properties),
             (join, from.Properties), (join, to.Properties)], mappings, relationshipMetadata, reasons, comparers)) return null;
        return new(name, variables[navigation.DeclaringEntityType], variables[navigation.TargetEntityType], true, false,
            InMemoryNavigationKeysSource(from.PrincipalKey.Properties), InMemoryNavigationKeysSource(to.PrincipalKey.Properties), assignment,
            new(CSharpTypeName(join.ClrType), variables[join], InMemoryNavigationKeysSource(from.Properties), InMemoryNavigationKeysSource(to.Properties),
                from.IsRequired, to.IsRequired, preserveMissingTargets, sourceComparison, targetComparison));
    }
}
