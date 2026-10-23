using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryTranslations;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryTranslations;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class PatternCatalogRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryTranslations")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryTranslations")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryTranslations")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries().GenerateOnlyConfiguredEntities());
        modelBuilder.HasInMemoryTranslation<string?, string?, bool>("OrdinalIgnoreCasePatterns",
            (value, pattern) => EF.Functions.Like(value, pattern),
            (value, pattern) => InMemoryStringPatterns.Like(value, pattern, StringComparison.OrdinalIgnoreCase));
        modelBuilder.HasInMemoryTranslation<string?, string?, bool>("EscapedOrdinalIgnoreCasePatterns",
            (value, pattern) => EF.Functions.Like(value, pattern, "!"),
            (value, pattern) => InMemoryStringPatterns.Like(value, pattern, StringComparison.OrdinalIgnoreCase, '!'));
        modelBuilder.HasInMemoryTranslation<string, int>("FixtureLength", value => PatternFunctions.Length(value), value => value.Length);
        modelBuilder.Entity<StoredPatternEntry>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IPatternCatalog>();
            entity.HasInMemoryStore<PatternSeed, int>(row => row.Id, row => new PatternSeed(row.Id, row.Title, row.Summary, row.Price, row.Hidden))
                .MapInMemoryEntity<PatternSeed>(row => new StoredPatternEntry { Id = row.Id, Title = row.Title, Summary = row.Summary, Price = row.Price, Hidden = row.Hidden });
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasProjection(row => new PatternCard(row.Id, row.Title, PatternFunctions.Length(row.Title)));
            entity.HasQuery("Search").Validate<PatternSearch>(request => request.Pattern != null && request.MinimumLength >= 0, "Supply a pattern and a nonnegative minimum length.")
                .Where<PatternSearch>((row, request) => EF.Functions.Like(row.Title, request.Pattern) && PatternFunctions.Length(row.Title) >= request.MinimumLength)
                .OrderBy(row => row.Id).ProjectTo<PatternCard>().List();
            entity.HasQuery("Summaries").Where<string>((row, pattern) => EF.Functions.Like(row.Summary, pattern)).OrderBy(row => row.Id).Select(row => row.Id).List();
            entity.HasQuery("Escaped").Where<string>((row, pattern) => EF.Functions.Like(row.Title, pattern, "!")).OrderBy(row => row.Id).Select(row => row.Id).List();
            entity.HasQuery("Totals").GroupBy(row => PatternFunctions.Length(row.Title),
                group => new PatternTotals(group.Key, group.Count(), group.Sum(row => row.Price))).Results<PatternTotals>().OrderBy(row => row.Length).List();
            entity.HasQuery("Stream").Where<PatternSearch>((row, request) => EF.Functions.Like(row.Title, request.Pattern))
                .OrderBy(row => row.Id).ProjectTo<PatternCard>().Stream();
        });
    }
}
