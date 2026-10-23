using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredCollectionGuardContext(DbContextOptions<MemoryCollectionContext> options, string mode) : FilteredCollectionContext(options)
{
    public string Mode { get; } = mode;
    public string FilterValue => throw new InvalidOperationException(GetType().Name + " must not execute a context getter during generation.");
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var child = modelBuilder.Entity<MemoryCollectionChild>();
        switch (Mode)
        {
            case "context": child.HasQueryFilter(row => row.Text != FilterValue); break;
            case "capture": var excluded = "deleted"; child.HasQueryFilter(row => row.Text != excluded); break;
            case "clock": child.HasQueryFilter(row => row.Text != DateTime.UtcNow.ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
            case "navigation": child.HasQueryFilter(row => row.Parent != null && row.Parent.OwnerId > 0); break;
            case "helper": child.HasQueryFilter(row => FilteredCollectionHelpers.Keep(row.Text)); break;
            case "autoinclude": child.Navigation(row => row.Parent).AutoInclude(); break;
        }
    }
}
