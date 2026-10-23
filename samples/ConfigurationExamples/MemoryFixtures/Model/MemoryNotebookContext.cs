using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryNotebook;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNotebook;

public sealed class MemoryNotebookContext(DbContextOptions<MemoryNotebookContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MemoryNote>().Ignore(note => note.Labels);
        modelBuilder.Entity<MemoryLabel>().HasKey(label => label.Code);
    }
}
