using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalLinks;

public sealed class LinkedNoteContext(DbContextOptions<LinkedNoteContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LinkedNote>().HasKey(row => new { row.TenantId, row.Id });
        modelBuilder.Entity<NoteLabel>().Property(row => row.Id).ValueGeneratedNever();
        modelBuilder.Entity<LinkedNote>().HasMany(row => row.Labels).WithMany(row => row.Notes).UsingEntity<NoteLabelLink>(
            right => right.HasOne<NoteLabel>().WithMany().HasForeignKey(row => row.LabelId).IsRequired(false),
            left => left.HasOne<LinkedNote>().WithMany().HasForeignKey(row => new { row.TenantId, row.NoteId }).IsRequired(),
            link => { link.HasKey(row => row.Id); link.Property(row => row.Id).ValueGeneratedNever(); });
    }
}
