using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class CreateManyContext(DbContextOptions<CreateManyContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CreateManyParent>().Property(row => row.Id).ValueGeneratedNever();
        var entity = modelBuilder.Entity<CreateManyRow>();
        entity.HasIndex(row => row.Name).IsUnique();
        entity.Property(row => row.Label).HasDefaultValue("fixture");
        entity.HasOne(row => row.Parent).WithMany(row => row.Rows).HasForeignKey(row => row.ParentId).IsRequired();
    }
}
