using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NativeFilterGuardContext(DbContextOptions<NativeFilterGuardContext> options, string mode) : DbContext(options)
{
    internal string Mode { get; } = mode;
    public string Tenant => throw new InvalidOperationException("Generation must not resolve a context value: " + Mode);
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ModelWriteFilterNote>(); entity.Property(row => row.Id).ValueGeneratedNever(); entity.Property(row => row.Version).IsConcurrencyToken();
        switch (Mode)
        {
            case "context": entity.HasQueryFilter(row => row.Title == Tenant); break;
            case "capture": var title = "deleted"; entity.HasQueryFilter(row => row.Title != title); break;
            case "clock": entity.HasQueryFilter(row => row.Version != DateTime.Now.Hour); break;
            case "helper": entity.HasQueryFilter(row => ModelFilterGuardCalls.Accept(row.Title)); break;
            case "shadow": entity.Property<bool>("ShadowDeleted"); entity.HasQueryFilter(row => !EF.Property<bool>(row, "ShadowDeleted")); break;
            case "plain": break;
            default: throw new InvalidOperationException("Unknown native-filter mode.");
        }
    }
}
