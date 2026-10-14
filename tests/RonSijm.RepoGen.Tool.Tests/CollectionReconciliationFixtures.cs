using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CollectionParent
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string Locked { get; set; } = "";
    public int Version { get; set; } = 1;
    public ICollection<CollectionChild> Children { get; set; } = new List<CollectionChild>();
    public ICollection<CollectionAttachment> Attachments { get; set; } = new List<CollectionAttachment>();
}

public sealed class CollectionChild
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public CollectionParent? Parent { get; set; }
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public string ProtectedNote { get; set; } = "";
    public string Locked { get; set; } = "";
    public bool Hidden { get; set; }
    public int Version { get; set; } = 1;
}

public sealed class CollectionContext(DbContextOptions<CollectionContext> options) : DbContext(options)
{
    public int DisposeCount { get; private set; }
    public bool HideHiddenChildren { get; set; }
    public DbSet<CollectionParent> Parents => Set<CollectionParent>();
    public DbSet<CollectionChild> Children => Set<CollectionChild>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CollectionParent>().Property(parent => parent.Version).IsConcurrencyToken();
        modelBuilder.Entity<CollectionParent>().Property(parent => parent.Locked).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        modelBuilder.Entity<CollectionChild>().HasIndex(child => new { child.ParentId, child.Key }).IsUnique();
        modelBuilder.Entity<CollectionChild>().HasIndex(child => new { child.ParentId, child.Text }).IsUnique();
        modelBuilder.Entity<CollectionChild>().Property(child => child.Version).IsConcurrencyToken();
        modelBuilder.Entity<CollectionChild>().Property(child => child.Locked).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        modelBuilder.Entity<CollectionChild>().HasQueryFilter(child => !HideHiddenChildren || !child.Hidden);
        modelBuilder.Entity<CollectionAttachment>().HasIndex(child => new { child.ParentId, child.Key }).IsUnique();
    }

    public override ValueTask DisposeAsync()
    {
        DisposeCount++;
        return base.DisposeAsync();
    }
}

public sealed record CollectionItem(string Key, string Text, int ExpectedVersion = 1);
public sealed record CollectionRequest(int ParentId, int OwnerId, IReadOnlyList<CollectionItem> Items, int ExpectedVersion);
public sealed record OptionalCollectionRequest(int ParentId, Optional<IReadOnlyList<CollectionItem>?> Items);
public sealed record CollectionSummary(int Version, string[] Keys);

public sealed class CollectionAttachment
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public CollectionParent? Parent { get; set; }
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
}
