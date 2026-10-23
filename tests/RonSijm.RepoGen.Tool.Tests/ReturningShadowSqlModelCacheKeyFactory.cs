using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningShadowSqlModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context.GetType(), ((ReturningShadowSqlContext)context).Table, designTime);
}
