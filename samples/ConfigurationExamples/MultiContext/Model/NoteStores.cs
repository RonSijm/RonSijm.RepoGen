// This example's EF model; compiled by the shared Models project.
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MultiContext;

// The same entity CLR type deliberately belongs to two independent models/databases.
public sealed class StoredNote
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public bool Active { get; set; }
}

public sealed class PrimaryNoteContext(DbContextOptions<PrimaryNoteContext> options) : DbContext(options)
{
    public DbSet<StoredNote> Notes => Set<StoredNote>();
}

public sealed class ArchiveNoteContext(DbContextOptions<ArchiveNoteContext> options) : DbContext(options)
{
    public DbSet<StoredNote> Notes => Set<StoredNote>();
}
