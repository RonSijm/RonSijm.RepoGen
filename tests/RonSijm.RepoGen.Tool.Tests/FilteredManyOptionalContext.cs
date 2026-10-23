using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class FilteredManyOptionalContext(DbContextOptions<ManyReadContext> options) : FilteredManyReadContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ManyReadLink>().HasOne<ManyReadTag>().WithMany().HasForeignKey(row => row.TagId).IsRequired(false);
    }
}
