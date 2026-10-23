using RonSijm.RepoGen.ConfigurationExamples.Contracts.ContractNotes;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ContractNotesRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ContractNotes")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.ContractNotes")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.ContractNotes")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries()
            .GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<ContractNote>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IContractNotes>();
            entity.HasInMemoryStore(row => row.Id, row => new ContractNote
                { Id = row.Id, Name = row.Name, Category = row.Category, Hidden = row.Hidden });
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasProjection(row => new ContractNoteSummary { Id = row.Id, Name = row.Name });
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).ProjectTo<ContractNoteSummary>().SingleOrDefault();
            entity.HasQuery("Search").Where<string>((row, term) => row.Name.Contains(term))
                .OrderBy(row => row.Id).ProjectTo<ContractNoteSummary>().List();
            entity.HasQuery("Page").OrderBy(row => row.Id).RequirePagingOrder().ProjectTo<ContractNoteSummary>().Paged();
            entity.HasQuery("Names").OrderBy(row => row.Id).ProjectTo<ContractNoteSummary>().Results<ContractNoteSummary>()
                .Dictionary(row => row.Id, row => row.Name);
            entity.HasQuery("Stream").OrderBy(row => row.Id).ProjectTo<ContractNoteSummary>().Stream();
        });
    }
}
