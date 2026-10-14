using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CompositeCollectionContext(DbContextOptions<CompositeCollectionContext> options, string scenario = "valid") : DbContext(options)
{
    public string Scenario { get; } = scenario;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var parent = modelBuilder.Entity<CompositeCollectionParent>();
        parent.HasKey(row => new { row.TenantId, row.Id });
        parent.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        var child = modelBuilder.Entity<CompositeCollectionChild>();
        child.Property(row => row.Id).ValueGeneratedNever();
        child.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        child.HasOne(row => row.Parent).WithMany(row => row.Children).HasForeignKey(row => new { row.TenantId, row.ParentId });
        var index = Scenario switch
        {
            "global" => child.HasIndex(row => new { row.Code, row.Language }),
            "subset" => child.HasIndex(row => new { row.TenantId, row.ParentId, row.Code }),
            "extra" => child.HasIndex(row => new { row.TenantId, row.ParentId, row.Code, row.Language, row.Text }),
            _ => child.HasIndex(row => new { row.TenantId, row.ParentId, row.Code, row.Language })
        };
        index.IsUnique(Scenario != "nonunique");
        if (Scenario == "filtered") index.HasFilter("Hidden = 0");
        if (Scenario == "nullable") child.Property(row => row.Language).IsRequired(false);
        if (Scenario == "generated") child.Property(row => row.Language).ValueGeneratedOnAdd();
        if (Scenario == "converted") child.Property(row => row.Language).HasConversion(value => value.ToUpperInvariant(), value => value);
    }
}
