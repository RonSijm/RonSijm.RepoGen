using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyContext(DbContextOptions<MappedPropertyContext> options) : DbContext(options)
{
    public const string Token = "Version ] \"; ignored --";
    public DbSet<MappedPropertyNote> Notes => Set<MappedPropertyNote>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var note = modelBuilder.Entity<MappedPropertyNote>(); note.Property(row => row.Id).ValueGeneratedNever();
        note.Property<string>("Code").HasColumnName("code \" quoted").IsRequired(); note.HasIndex("Code").IsUnique();
        note.Property<int>(Token).HasColumnName("version ] \" quoted").HasComputedColumnSql("\"Counter\" + 1", stored: true).IsConcurrencyToken();
        note.Property<int?>("OptionalVersion").IsConcurrencyToken();
        note.Property<byte[]?>("BinaryVersion").IsConcurrencyToken();
        note.Property<ReturningState>("State").HasConversion<string>().IsConcurrencyToken();
        note.Property<bool>("Hidden").HasDefaultValue(false);
        note.HasQueryFilter(row => !EF.Property<bool>(row, "Hidden"));
    }
}
