using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeFilterCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => context is NativeFilterGuardContext native
        ? (context.GetType(), native.Mode, designTime) : (context.GetType(), "", designTime);
}
