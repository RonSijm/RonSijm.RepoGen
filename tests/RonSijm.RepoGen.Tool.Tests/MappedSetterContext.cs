using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedSetterContext(DbContextOptions<MappedSetterContext> options) : DbContext(options)
{
    public const string Version = "Version ] \"; not SQL --";
    public const string Extra = "Extra ] \"; not SQL --";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<MappedSetterNote>();
        note.Property(row => row.Id).ValueGeneratedNever();
        note.Property(row => row.Caption).HasField("caption").UsePropertyAccessMode(PropertyAccessMode.Field);
        note.Property<int>("audit").HasField("audit");
        note.Property<int>(Version).IsConcurrencyToken();
        note.IndexerProperty<string?>(Extra);
        note.HasIndex(row => row.UniqueCode).IsUnique();
        note.HasOne<MappedSetterOwner>().WithMany().HasForeignKey(row => row.OwnerId);
    }
}
