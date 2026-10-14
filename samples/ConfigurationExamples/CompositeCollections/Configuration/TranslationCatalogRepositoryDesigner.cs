// TranslationCatalogContext -> the contract facade and this example's persistence/testing/manifest outputs.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.CompositeCollections;
using RonSijm.RepoGen.ConfigurationExamples.Models.CompositeCollections;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class TranslationCatalogRepositoryDesigner : RepositoryDesigner
{
    private static readonly bool[] SaveModes = [false, true];

    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.CompositeCollections")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.CompositeCollections")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.CompositeCollections")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<LocalizedDocument>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<DocumentSeed, (int, int)>(row => new ValueTuple<int, int>(row.TenantId, row.Id), row => new(row.TenantId, row.Id, row.Version))
                .MapInMemoryEntity<DocumentSeed>(row => new LocalizedDocument { TenantId = row.TenantId, Id = row.Id, Version = row.Version })
                .MapInMemoryWrites<DocumentSeed>((row, saved) => new(row.TenantId, row.Id, row.Version));
            foreach (var save in SaveModes)
            {
                var command = entity.HasCommand(save ? "SaveTranslations" : "StageTranslations")
                    .Where<TranslationChange>((row, request) => row.TenantId == request.TenantId && row.Id == request.Id)
                    .Patch(setters => setters.SetProperty(row => row.Version, row => row.Version + 1)).AllowWrite(row => row.Version)
                    .ExpectVersion<int, TranslationChange>(row => row.Version, (row, request) => request.ExpectedVersion)
                    .ReconcileCollection<LocalizedText, TranslationInput, (string, string), TranslationChange>(
                        row => row.Texts, request => request.Texts,
                        row => new ValueTuple<string, string>(row.Code, row.Locale),
                        item => new ValueTuple<string, string>(item.Code, item.Locale),
                        children => children.MaximumItems(5)
                            .Add(item => new LocalizedText { Code = item.Code, Locale = item.Locale, Text = item.Text, Version = 1, Hidden = false })
                            .Update(setters => setters.SetProperty(row => row.Text, (row, item) => item.Text).SetProperty(row => row.Version, row => row.Version + 1))
                            .AllowWrite(row => row.Version).ExpectVersion(row => row.Version, (row, item) => item.ExpectedVersion)
                            .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
                    .SimulateCollectionWrites(); // Optional fixture lowering; production uses the same designed rules.
                if (save) command.Save().Returns(row => row.Version); else command.Stage();
            }
        });
        modelBuilder.Entity<LocalizedText>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasInMemoryStore<TranslationSeed, int>(row => row.Id, row => new(row.Id, row.TenantId, row.DocumentId, row.Code, row.Locale, row.Text, row.Version, row.Hidden))
                .MapInMemoryEntity<TranslationSeed>(row => new LocalizedText { Id = row.Id, TenantId = row.TenantId, DocumentId = row.DocumentId,
                    Code = row.Code, Locale = row.Locale, Text = row.Text, Version = row.Version, Hidden = row.Hidden })
                .MapInMemoryWrites<TranslationSeed>((row, saved) => new(row.Id, row.TenantId, row.DocumentId, row.Code, row.Locale, row.Text, row.Version, row.Hidden));
            entity.HasInMemoryGeneratedValue(row => row.Id, (row, existing) => checked(existing.Select(item => item.Id).DefaultIfEmpty().Max() + 1));
            entity.HasVisibilityPolicy("Visible", row => !row.Hidden);
            entity.HasQuery("ListTranslations").Where<TranslationAddress>((row, address) => row.TenantId == address.TenantId && row.DocumentId == address.DocumentId)
                .OrderBy(row => row.Code).ThenBy(row => row.Locale).Select(row => new TranslationView(row.Code, row.Locale, row.Text, row.Version)).List();
        });
    }
}
