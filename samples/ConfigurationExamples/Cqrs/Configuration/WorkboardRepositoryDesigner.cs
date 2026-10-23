// Paired with ../Model/WorkboardContext.cs by ../../GenerationRunner/Program.cs.
// Output: ../Generated/Repositories.g.cs (read/write selector ports) and ../Generated/Queries.g.cs.
// See ../README.md for the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.Workboard;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class WorkboardRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<WorkItem>(entity =>
        {
            entity.ProjectionEnabled = false;
            entity.HasProjection<WorkItemSummary>(item => new WorkItemSummary(item.Id, item.Title, item.Priority));
            entity.HasDefaultSort(item => item.Priority);
            entity.HasSortableFields(item => item.Id, item => item.Priority);
            entity.HasQuery("ListOpen").Where(item => !item.Completed)
                .OrderBy(item => item.Priority).ThenBy(item => item.Id).ProjectTo<WorkItemSummary>().List();
            entity.HasQuery("LoadForUpdate").OnWriteRepository().AsTracking()
                .Where<int>((item, id) => item.Id == id).SingleOrDefault();
            entity.HasCommand("Complete").Where<int>((item, id) => item.Id == id)
                .Patch(setters => setters.SetProperty(item => item.Completed, item => true)).Stage();
        });
        modelBuilder.Entity<WorkLabel>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasQuery("ListLabels").OrderBy(label => label.Name).ProjectTo(label => label.Name).List();
        });
        modelBuilder.Entity<WorkEvent>().ReadEnabled = false;
        // Keyless sources are automatically read-only; no write port or public write selector is generated.
        modelBuilder.Entity<WorkSummary>().HasQuery("ListWorkload").OrderBy(summary => summary.Priority).List();
    }
}
