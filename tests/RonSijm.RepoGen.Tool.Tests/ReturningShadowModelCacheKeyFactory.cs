using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((ReturningShadowContext)context).Shape, designTime);
}
