using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.ScriptedNotes;

public sealed class ScriptedNote
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class ScriptedNotesContext(DbContextOptions<ScriptedNotesContext> options) : DbContext(options)
{
    public DbSet<ScriptedNote> Notes => Set<ScriptedNote>();
}
