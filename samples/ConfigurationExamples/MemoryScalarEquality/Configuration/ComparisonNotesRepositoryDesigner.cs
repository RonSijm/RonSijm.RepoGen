using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScalarEquality;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryScalarEquality;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ComparisonNotesRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryScalarEquality")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryScalarEquality")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryScalarEquality")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands()
            .RequirePortableTestQueries().RequirePortableTestCommands());
        modelBuilder.Entity<ComparedNote>(entity =>
        {
            entity.BindReadPort<IComparisonNotesReader>().BindWritePort<IComparisonNotesWriter>();
            entity.HasInMemoryStore<ComparisonNoteSeed, int>(row => row.Id, row => new(row.Id, row.Caption, row.Revision))
                .MapInMemoryEntity<ComparisonNoteSeed>(row => new ComparedNote { Id = row.Id, Caption = row.Caption, Revision = row.Revision })
                .MapInMemoryWrites<ComparisonNoteSeed>((row, saved) => new(row.Id, row.Caption, row.Revision))
                // Fixture state/version equality is explicit, not copied or executed from EF metadata.
                .HasInMemoryValueEquality(row => row.Caption, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                .HasInMemoryValueEquality(row => row.Revision, (left, right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase));
            entity.HasQuery("Find").Where<int>((row, id) => row.Id == id).Select(row => new ComparisonNoteSeed(row.Id, row.Caption, row.Revision)).SingleOrDefault();
            // This stays literal equality: neither the fixture policy nor EF's CLR comparer changes this predicate.
            entity.HasQuery("Matching").Where<string>((row, caption) => row.Caption == caption).OrderBy(row => row.Id).Select(row => row.Id).List();
            entity.HasCommand("StageCaption").Where<ComparisonNoteChange>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string?, ComparisonNoteChange>(row => row.Caption, (row, request) => request.Caption)).Stage();
            entity.HasCommand("SetCaption").Where<ComparisonNoteChange>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string?, ComparisonNoteChange>(row => row.Caption, (row, request) => request.Caption))
                .Save().ReturnsSaved(row => new ComparisonNoteSeed(row.Id, row.Caption, row.Revision));
            entity.HasCommand("Rename").Where<ComparisonNoteChange>((row, request) => row.Id == request.Id)
                .Patch(setters => setters.SetProperty<string?, ComparisonNoteChange>(row => row.Caption, (row, request) => request.Caption)
                    .SetProperty<string, ComparisonNoteChange>(row => row.Revision, (row, request) => request.Revision))
                .AllowWrite(row => row.Revision).ExpectVersion<string, ComparisonNoteChange>(row => row.Revision, (row, request) => request.ExpectedRevision)
                .Save().ReturnsSaved(row => new ComparisonNoteSeed(row.Id, row.Caption, row.Revision));
            entity.HasCommand("Delete").Where<ComparisonNoteChange>((row, request) => row.Id == request.Id).TrackedDelete()
                .ExpectVersion<string, ComparisonNoteChange>(row => row.Revision, (row, request) => request.ExpectedRevision).Save().Returns(row => row.Id);
        });
    }
}
