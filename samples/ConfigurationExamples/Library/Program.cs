// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/LibraryRepositoryDesigner.cs and README.md.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated;
using RonSijm.RepoGen.ConfigurationExamples.Models.Library;
using RonSijm.RepoGen.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Library;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<LibraryContext>().UseSqlite(connection).Options;
        await using (var seed = new LibraryContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.Books.Add(new Book
            {
                Id = 1,
                Title = "A tiny library",
                Author = new Author { Name = "Ada", Address = new AuthorAddress { City = "Amsterdam" } },
                Chapters =
                [
                    new Chapter { Number = 1, Title = "Introduction", Notes = [new ChapterNote { Text = "Not requested", IsPublic = true }] },
                    new Chapter { Number = 2, Title = "Loading", Notes = [new ChapterNote { Text = "Public note", IsPublic = true }, new ChapterNote { Text = "Internal note" }] }
                ],
                Reviews = [new Review { Rating = 5, Text = "Great" }, new Review { Rating = 2, Text = "Not requested" }]
            });
            seed.Books.Add(new Book { Id = 2, Title = "No author" });
            await seed.SaveChangesAsync();
        }

        var factory = new LibraryContextFactory(options);
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<LibraryContext>>(factory);
        services.AddScoped<IDbContextLeaseProvider<LibraryContext>, FactoryDbContextLeaseProvider<LibraryContext>>();
        services.AddLibraryContextRepositories();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadBookRepository>();
        var applicationPort = scope.ServiceProvider.GetRequiredService<IBookLibrary>();
        if (!ReferenceEquals(repository, applicationPort))
        {
            throw new InvalidOperationException("The application port did not share the generated implementation.");
        }

        // The entire consumer query: no Include, ThenInclude, AsNoTracking or runtime profile flags.
        var request = new BookDetailsRequest(1, 2, 1, 4, false);
        var details = await applicationPort.GetBookDetailsAsync(request);
        if (details is null || details.Author != "Ada" || details.City != "Amsterdam" || details.Chapters.Count != 1
            || details.Chapters[0].Number != 2 || !details.Chapters[0].Notes.SequenceEqual(["Public note"])
            || !details.Reviews.SequenceEqual(["Great"]))
        {
            throw new InvalidOperationException("The configured graph or request-bound filters were not applied.");
        }

        var internalDetails = await repository.GetBookDetailsAsync(request with { MaximumNotesPerChapter = 10, IncludeInternalNotes = true });
        if (internalDetails?.Chapters[0].Notes.Count != 2)
        {
            throw new InvalidOperationException("The second request did not rebind loading inputs.");
        }

        if (await repository.GetBookDetailsAsync(request with { Id = 999 }) is not null)
        {
            throw new InvalidOperationException("Missing book should return null.");
        }

        var authorNames = await applicationPort.GetAuthorNamesAsync(null);
        if (!authorNames.SequenceEqual(new string?[] { "Ada", null })
            || !(await applicationPort.GetAuthorNamesAsync(new string?[] { null, "No author" })).SequenceEqual(new string?[] { null })
            || (await applicationPort.GetAuthorNamesAsync(Array.Empty<string?>())).Count != 0)
        {
            throw new InvalidOperationException("Nullable input elements, null/empty input semantics or nullable projected results were lost.");
        }

        if (factory.Contexts.Count != 6 || factory.Contexts.Any(static context => context.DisposeCount != 1 || context.TrackedAtDisposal != 0))
        {
            throw new InvalidOperationException("Read leases were not disposed exactly once or tracked entities unexpectedly.");
        }

        // The detached DTO graph is still usable after its owned context was disposed.
        Console.WriteLine($"Library: {details.Title}, nested/filtered profiles, nullable collection port and owned read leases passed.");
    }
}
