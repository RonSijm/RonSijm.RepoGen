using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryMappedWrites;

public sealed class RevisionNotebookContext(DbContextOptions<RevisionNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<RevisionNote>();
        note.Property(row => row.Id).ValueGeneratedNever();
        note.Property<int>("Revision").HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
    }
}
