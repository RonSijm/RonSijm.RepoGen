using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ProtectedNote
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int OwnerId { get; set; }
    public string Title { get; set; } = "";
    public string Origin { get; set; } = "";
    public int Version { get; set; } = 1;
    [NotMapped] public string Ignored { get; set; } = "";
    public ICollection<ProtectedNotePart> Parts { get; set; } = new List<ProtectedNotePart>();
}

public sealed class ProtectedNotePart
{
    public int Id { get; set; }
    public int NoteId { get; set; }
    public ProtectedNote Note { get; set; } = null!;
    public string Key { get; set; } = "";
    public string Origin { get; set; } = "";
}

public sealed class WriteProtectionContext(DbContextOptions<WriteProtectionContext> options) : DbContext(options)
{
    public DbSet<ProtectedNote> Notes => Set<ProtectedNote>();
    public DbSet<ProtectedNotePart> Parts => Set<ProtectedNotePart>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProtectedNote>(note =>
        {
            note.HasKey(value => value.Id);
            note.HasIndex(value => value.Code).IsUnique();
            note.Property(value => value.Version).HasDefaultValue(1).IsConcurrencyToken();
            note.Property(value => value.Origin).HasDefaultValue("").ValueGeneratedNever();
            note.HasMany(value => value.Parts).WithOne(value => value.Note).HasForeignKey(value => value.NoteId);
        });
        modelBuilder.Entity<ProtectedNotePart>().HasIndex(part => new { part.NoteId, part.Key }).IsUnique();
    }
}

public sealed record ProtectedNoteRequest(string Code, string Title, string Origin, int OwnerId, int Id = 1, int ExpectedVersion = 1);
public sealed record ProtectedNoteView(int Id, int OwnerId, string Title, string Origin, int Version);
public sealed record ProtectedPartInput(string Key, string Origin);
public sealed record ProtectedPartsRequest(int Id, IReadOnlyList<ProtectedPartInput> Parts);
