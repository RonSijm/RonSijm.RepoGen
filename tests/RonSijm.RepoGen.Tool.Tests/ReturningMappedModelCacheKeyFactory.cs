using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningMappedModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((ReturningMappedContext)context).Shape, ((ReturningMappedContext)context).Table, designTime);
}
