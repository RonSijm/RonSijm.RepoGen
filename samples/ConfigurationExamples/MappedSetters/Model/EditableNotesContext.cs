using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MappedSetters;

public sealed class EditableNotesContext(DbContextOptions<EditableNotesContext> options) : DbContext(options)
{
    public const string Revision = "revision";
    public const string Remark = "editor remark";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<EditableNote>();
        note.Property(row => row.Id).ValueGeneratedNever();
        note.Property(row => row.Caption).HasField("caption").UsePropertyAccessMode(PropertyAccessMode.Field);
        note.Property<int>(Revision).IsConcurrencyToken();
        note.IndexerProperty<string?>(Remark);
    }
}
