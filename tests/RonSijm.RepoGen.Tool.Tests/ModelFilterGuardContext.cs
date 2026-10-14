using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ModelFilterGuardContext(DbContextOptions<ModelFilterGuardContext> options, string mode) : DbContext(options)
{
    internal string Mode { get; } = mode;
    public string Tenant => throw new InvalidOperationException("Generation must never resolve a tenant from a DbContext: " + Mode);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ModelFilterRow>(); entity.Property(row => row.Id).ValueGeneratedNever(); entity.Ignore(row => row.Unmapped);
        entity.HasOne(row => row.Folder).WithMany(folder => folder.Rows).HasForeignKey(row => row.FolderId);
        modelBuilder.Entity<ModelFilterFolder>().Property(folder => folder.Id).ValueGeneratedNever();
        switch (Mode)
        {
            case "context": entity.HasQueryFilter(row => row.Title == Tenant); break;
            case "capture": var text = "deleted"; entity.HasQueryFilter(row => row.Title != text); break;
            case "collection": var titles = new[] { "deleted" }; entity.HasQueryFilter(row => !titles.Contains(row.Title)); break;
            case "clock": entity.HasQueryFilter(row => row.Id != DateTime.Now.Hour); break;
            case "static": entity.HasQueryFilter(row => row.Id != ModelFilterGuardCalls.Current); break;
            case "helper": entity.HasQueryFilter(row => ModelFilterGuardCalls.Accept(row.Title)); break;
            case "identity": entity.HasQueryFilter(row => row.ToString() != string.Empty); break;
            case "navigation": entity.HasQueryFilter(row => row.Folder != null && !row.Folder.Archived); break;
            case "shadow": entity.Property<bool>("ShadowDeleted"); entity.HasQueryFilter(row => !EF.Property<bool>(row, "ShadowDeleted")); break;
            case "unmapped": entity.HasQueryFilter(row => row.Unmapped != string.Empty); break;
            case "autoinclude": entity.HasQueryFilter(row => !row.Deleted); entity.Navigation(row => row.Folder).AutoInclude(); break;
            default: throw new InvalidOperationException("Unknown model-filter test mode: " + Mode);
        }
    }
}
