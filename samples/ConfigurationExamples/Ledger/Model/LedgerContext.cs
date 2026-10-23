// This example's EF model; compiled by the shared Models project.
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.Ledger;

public sealed class LedgerContext(DbContextOptions<LedgerContext> options) : DbContext(options)
{
    public DbSet<LedgerEntry> Entries => Set<LedgerEntry>();
    public DbSet<LedgerSaveReceipt> SaveReceipts => Set<LedgerSaveReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.HasKey(entry => entry.Id);
            entity.HasIndex(entry => entry.Code).IsUnique();
            entity.Property(entry => entry.Version).IsConcurrencyToken();
            entity.Property(entry => entry.IsDeleted).HasDefaultValue(false);
            entity.Property(entry => entry.NormalizedCode).HasComputedColumnSql("upper(Code)", stored: true);
        });
    }
}

public sealed class LedgerEntry
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Text { get; set; }
    public int Version { get; set; }
    public bool IsDeleted { get; set; }
    public string NormalizedCode { get; private set; } = string.Empty;
}

// Infrastructure-owned rows: the allowlisted designer does not generate a repository for them.
public sealed class LedgerSaveReceipt
{
    public int Id { get; set; }
    public int ChangedEntries { get; set; }
}
