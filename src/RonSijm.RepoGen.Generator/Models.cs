using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace RonSijm.RepoGen.Generator;

internal sealed class ProjectionCandidate
{
    public ProjectionCandidate(
        INamedTypeSymbol projection,
        INamedTypeSymbol entity,
        string? methodName,
        Location location)
    {
        Projection = projection;
        Entity = entity;
        MethodName = methodName;
        Location = location;
    }

    public INamedTypeSymbol Projection { get; }

    public INamedTypeSymbol Entity { get; }

    public string? MethodName { get; }

    public Location Location { get; }
}

internal sealed class ProjectionModel
{
    public ProjectionModel(
        INamedTypeSymbol projection,
        string singleMethodName,
        string collectionMethodName,
        string expression,
        Location location)
    {
        Projection = projection;
        SingleMethodName = singleMethodName;
        CollectionMethodName = collectionMethodName;
        Expression = expression;
        Location = location;
    }

    public INamedTypeSymbol Projection { get; }

    public string SingleMethodName { get; }

    public string CollectionMethodName { get; }

    public string Expression { get; }

    public Location Location { get; }
}

internal sealed class EntityModel
{
    public EntityModel(INamedTypeSymbol entity, List<ProjectionModel> projections)
    {
        Entity = entity;
        Projections = projections;
    }

    public INamedTypeSymbol Entity { get; }

    public List<ProjectionModel> Projections { get; }
}