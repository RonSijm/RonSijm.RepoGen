using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.ContractNotes;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.ContractNotes;

public sealed class ContractNotesContext(DbContextOptions<ContractNotesContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<ContractNote>().Property(row => row.Id).ValueGeneratedNever();
}
