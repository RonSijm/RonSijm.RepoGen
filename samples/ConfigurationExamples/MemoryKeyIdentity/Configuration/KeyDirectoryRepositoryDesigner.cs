using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryKeyIdentity;
using RonSijm.RepoGen.Design;
using DictionaryEntry = RonSijm.RepoGen.ConfigurationExamples.Models.MemoryKeyIdentity.DictionaryEntry;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class KeyDirectoryRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryKeyIdentity")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryKeyIdentity")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryKeyIdentity")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<DictionaryEntry>(entity =>
        {
            entity.HasInMemoryStore<EntrySeed, string>(row => row.Key, row => new EntrySeed(row.Key, row.Caption))
                .MapInMemoryEntity<EntrySeed>(row => new DictionaryEntry { Code = row.Key, Name = row.Caption })
                .MapInMemoryWrites<EntrySeed>((row, saved) => new EntrySeed(row.Code, row.Name))
                // Opt-in fixture key identity only; remove this line for ordinary default string equality.
                .HasInMemoryKeyComparer<string>(() => StringComparer.OrdinalIgnoreCase);
            entity.HasProjection(row => new EntryView(row.Code, row.Name));
            // This remains an ordinary predicate. It is not rewritten to use the fixture key comparer.
            entity.HasQuery("Find").Where<string>((row, key) => row.Code == key).ProjectTo<EntryView>().SingleOrDefault();
            entity.HasCommand("StageCreate").Create<EntryChange>(request => new DictionaryEntry { Code = request.Key, Name = request.Caption })
                .AllowWrite(row => row.Code).Stage();
            entity.HasCommand("Rename").Where<EntryChange>((row, request) => row.Code == request.Key)
                .Patch(setters => setters.SetProperty<string, EntryChange>(row => row.Name, (row, request) => request.Caption)).Save();
            entity.HasCommand("Remove").Where<string>((row, key) => row.Code == key).TrackedDelete().Save();
        });
    }
}
