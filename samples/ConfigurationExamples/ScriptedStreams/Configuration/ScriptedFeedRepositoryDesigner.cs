// Paired with ../Model/ScriptedFeedContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/ScriptedFeedRepositories.g.cs; test fixture: ../Generated/TestDoubles.g.cs.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.ScriptedFeed;
using RonSijm.RepoGen.ConfigurationExamples.Models.ScriptedFeed;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ScriptedFeedRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ScriptedFeed")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.ScriptedFeed")
            .GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<FeedEntry>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasQuery("Watch").Where<int>((entry, afterId) => entry.Id > afterId)
                .OrderBy(entry => entry.Id).ProjectTo(entry => new FeedItem(entry.Id, entry.Text)).Stream();
        });
    }
}
