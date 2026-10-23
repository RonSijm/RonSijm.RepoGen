using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedKeyCollisionContext(DbContextOptions<MappedKeyCollisionContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var owner = modelBuilder.Entity<MappedKeyCollisionRow>();
        owner.Property<int>("Rank");
        owner.HasAlternateKey(row => row.__repoGenProperty0);
        modelBuilder.Entity<MappedKeyCollisionChild>().HasOne<MappedKeyCollisionRow>().WithMany(row => row.Children)
            .HasForeignKey("OwnerId").HasPrincipalKey(row => row.__repoGenProperty0);
    }
}
