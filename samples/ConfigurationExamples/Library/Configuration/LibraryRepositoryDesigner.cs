// Paired with ../Model/LibraryContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/LibraryRepositories.g.cs; implementation: ../Generated/Repositories.g.cs.
// See ../README.md for the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.Library;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class LibraryRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated")
            .IncludeApiDataContracts()
            .UseAsyncContextLeases()
            .GenerateOnlyConfiguredEntities());

        modelBuilder.Entity<Book>(entity =>
        {
            entity.WriteEnabled = false;
            entity.ProjectionEnabled = false;
            entity.BindReadPort<IBookLibrary>();

            var summary = entity.HasLoadProfile("Summary", load =>
                load.Include(book => book.Author).ThenInclude(author => author!.Address));
            entity.HasLoadProfile<BookDetailsRequest>("Details", load =>
            {
                load.Include((book, request) => book.Chapters.Where(chapter => chapter.Number >= request.FirstChapter)
                        .OrderBy(chapter => chapter.Number))
                    .ThenInclude((chapter, request) => chapter.Notes.Where(note => note.IsPublic || request.IncludeInternalNotes)
                        .OrderBy(note => note.Id).Take(request.MaximumNotesPerChapter));
                load.Include((book, request) => book.Reviews.Where(review => review.Rating >= request.MinimumRating));
            });
            entity.HasProjection(book => LibraryMapping.Details(book)).AutoIncludeNavigations(false);

            // Loading, request binding and SQL strategy stay in the designer. A service calls one method.
            entity.HasQuery("GetBookDetails").UseLoadProfile(summary).UseLoadProfile("Details")
                .Where<BookDetailsRequest>((book, request) => book.Id == request.Id)
                .AsSplitQuery().RequireExplicitLoadStrategy()
                .ProjectTo<BookDetails>().SingleOrDefault();

            // Annotations alone do not choose behavior: null means all, an empty list means none,
            // and a book without an author contributes a null result, all declared here.
            entity.HasQuery("GetAuthorNames")
                .InputNullability<IReadOnlyList<string>>("titles", type => type.Nullable().TypeArgument(0, item => item.Nullable()))
                .Where<IReadOnlyList<string?>>((book, titles) => titles == null || titles.Contains(book.Title))
                .OrderBy(book => book.Id).Select(book => book.Author == null ? null : book.Author.Name)
                .ResultNullability<string>(type => type.Nullable()).List();
        });
    }
}
