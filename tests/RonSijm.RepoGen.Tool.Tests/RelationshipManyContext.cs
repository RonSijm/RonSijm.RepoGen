using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class RelationshipManyContext(DbContextOptions<ManyReadContext> options) : ManyReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ManyReadOwner>().Property(row => row.Code).UseCollation("NOCASE");
        modelBuilder.Entity<ManyReadLink>().Property(row => row.OwnerCode).UseCollation("NOCASE");
    }
}
