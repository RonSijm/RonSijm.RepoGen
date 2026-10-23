// Paired with ../Model/LookupDirectoryContext.cs by ../../GenerationRunner/Program.cs.
// Output: ../Generated/Repositories.g.cs (IReadDirectoryEntryRepository) and ../Generated/Queries.g.cs.
// See ../README.md for the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.TaskDesk;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class LookupDirectoryRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors().GenerateOnlyConfiguredEntities());
        var code = modelBuilder.HasTypeAdapter<int, DirectoryCode>(value => new DirectoryCode(value), value => value.Value);
        modelBuilder.Entity<DirectoryEntry>(entity =>
        {
            entity.WriteEnabled = false;
            entity.ProjectionEnabled = false;
            entity.HasAccessor(entry => entry.Id).UseValueConverter<Guid>();
            entity.HasAccessor(entry => entry.Code).UseAdapter(code);
            entity.HasProjection(entry => new DirectorySummary(entry.Id.Value, entry.Name));
            entity.HasQuery("ListEntries").OrderBy(entry => entry.Name).ProjectTo<DirectorySummary>().List();
        });
    }
}
