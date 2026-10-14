// This example's EF model; compiled by the shared Models project.
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.Inventory;

public sealed class InventoryContext(DbContextOptions<InventoryContext> options) : DbContext(options)
{
    public DbSet<InventoryItem> Items => Set<InventoryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InventoryItem>().Property(item => item.Version).IsConcurrencyToken();
        modelBuilder.Entity<InventoryItem>().ToTable("InventoryItems", table => table.HasCheckConstraint("NonNegativeStock", "Quantity >= 0"));
    }
}

public sealed class InventoryItem
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Category { get; set; } = "";
    public int Quantity { get; set; }
    public int Version { get; set; } = 1;
}
