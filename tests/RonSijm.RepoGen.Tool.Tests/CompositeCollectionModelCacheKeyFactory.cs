using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is CompositeCollectionContext composite
        ? (context.GetType(), composite.Scenario, designTime) : (object)(context.GetType(), designTime);
}
