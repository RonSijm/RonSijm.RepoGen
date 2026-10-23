using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.ScriptedFeed;

public sealed class FeedEntry
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
}

public sealed class ScriptedFeedContext(DbContextOptions<ScriptedFeedContext> options) : DbContext(options)
{
    public DbSet<FeedEntry> Entries => Set<FeedEntry>();
}
