// This example's EF model; compiled by the shared Models project.
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.Visibility;

public sealed class VisibilityContext(DbContextOptions<VisibilityContext> options) : DbContext(options)
{
    public DbSet<ShowcaseDocument> Documents => Set<ShowcaseDocument>();
    public DbSet<ShowcaseOwner> Owners => Set<ShowcaseOwner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShowcaseOwner>().HasQueryFilter(owner => owner.Active);
        modelBuilder.Entity<ShowcaseDocument>().HasOne(document => document.Owner).WithMany().HasForeignKey(document => document.OwnerId);
        modelBuilder.Entity<ShowcaseDocument>().HasMany(document => document.Comments).WithOne().HasForeignKey(comment => comment.DocumentId);
        modelBuilder.Entity<ShowcaseDocument>().Property(document => document.Id).ValueGeneratedNever();
        modelBuilder.Entity<ShowcaseOwner>().Property(owner => owner.Id).ValueGeneratedNever();
        modelBuilder.Entity<ShowcaseComment>().Property(comment => comment.Id).ValueGeneratedNever();
    }
}

public sealed class ShowcaseDocument
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool Active { get; set; }
    public int? OwnerId { get; set; }
    public ShowcaseOwner? Owner { get; set; }
    public List<ShowcaseComment> Comments { get; set; } = [];
}

public sealed class ShowcaseOwner
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; }
}

public sealed class ShowcaseComment
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public string Text { get; set; } = "";
    public bool Public { get; set; }
}

public static class VisibilityMapping
{
    public static ReviewedDocumentDetails Details(ShowcaseDocument document) => new(document.Id, document.Title, document.Owner?.Name,
        document.Comments.Select(comment => comment.Text).ToArray());
}
