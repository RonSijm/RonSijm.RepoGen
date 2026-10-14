using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class BatchRow
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Origin { get; set; } = "Keep";
    public int OwnerId { get; set; }
    public BatchOwner Owner { get; set; } = null!;
    public int Version { get; set; } = 1;
    public bool Hidden { get; set; }
}

public sealed class BatchOwner
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class BatchContext(DbContextOptions<BatchContext> options) : DbContext(options)
{
    public DbSet<BatchRow> Rows => Set<BatchRow>();
    public int Saves { get; private set; }
    public int Disposals { get; private set; }
    public void ResetSaves() => Saves = 0;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BatchRow>().HasIndex(row => row.Code).IsUnique();
        modelBuilder.Entity<BatchRow>().Property(row => row.Version).IsConcurrencyToken();
        modelBuilder.Entity<BatchRow>().HasQueryFilter(row => !row.Hidden);
        modelBuilder.Entity<BatchRow>().HasOne(row => row.Owner).WithMany().HasForeignKey(row => row.OwnerId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return base.SaveChangesAsync(cancellationToken);
    }

    public override ValueTask DisposeAsync()
    {
        Disposals++;
        return base.DisposeAsync();
    }
}

public sealed record BatchItem(string Code, string Title, int ExpectedVersion = 1, bool Supplied = true);
public sealed record BatchRequest(int OwnerId, IEnumerable<BatchItem> Items);
public sealed record BatchRowView(string Code, string Title, int Version);
public sealed record BatchNullableRowView(string Code, string? Title);
public readonly record struct BatchCode(string Value);
public sealed record StrongBatchItem(BatchCode Code, string Title, int ExpectedVersion);
public sealed record StrongBatchRequest(int OwnerId, IReadOnlyList<StrongBatchItem> Items);
public sealed record StrongBatchView(BatchCode Code, string Title, int Version);
public interface IBatchWritePort
{
    Task<RonSijm.RepoGen.Abstractions.CommandResult<IReadOnlyList<BatchRowView>>> RenameAsync(BatchRequest request, CancellationToken cancellationToken = default);
    Task<RonSijm.RepoGen.Abstractions.CommandResult> StageAsync(BatchRequest request, CancellationToken cancellationToken = default);
    Task<RonSijm.RepoGen.Abstractions.CommandResult<IReadOnlyList<BatchRowView>>> DeleteAsync(BatchRequest request, CancellationToken cancellationToken = default);
}
