// This example's EF model; compiled by the shared Models project.
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.Library;

public sealed class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Author? Author { get; set; }
    public List<Chapter> Chapters { get; set; } = [];
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}

public sealed class Author
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AuthorAddress? Address { get; set; }
}

public sealed class AuthorAddress
{
    public int Id { get; set; }
    public string City { get; set; } = string.Empty;
}

public sealed class Chapter
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public ICollection<ChapterNote> Notes { get; set; } = new List<ChapterNote>();
}

public sealed class ChapterNote
{
    public int Id { get; set; }
    public int ChapterId { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
}

public sealed class Review
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Rating { get; set; }
}

// Final DTO conversion deliberately consumes the loaded graph. This is not a SQL projection:
// a pure Select over navigations must contain its own filters instead of relying on Include.
public static class LibraryMapping
{
    public static BookDetails Details(Book book) => new(book.Id, book.Title, book.Author?.Name, book.Author?.Address?.City,
        book.Chapters.OrderBy(chapter => chapter.Number)
            .Select(chapter => new ChapterDetails(chapter.Number, chapter.Title, chapter.Notes.OrderBy(note => note.Id).Select(note => note.Text).ToArray())).ToArray(),
        book.Reviews.OrderBy(review => review.Id).Select(review => review.Text).ToArray());
}

public sealed class LibraryContext(DbContextOptions<LibraryContext> options) : DbContext(options)
{
    public DbSet<Book> Books => Set<Book>();
    public int DisposeCount { get; private set; }
    public int TrackedAtDisposal { get; private set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Book>().HasMany(book => book.Chapters).WithOne().HasForeignKey(chapter => chapter.BookId);
        modelBuilder.Entity<Book>().HasMany(book => book.Reviews).WithOne().HasForeignKey(review => review.BookId);
        modelBuilder.Entity<Chapter>().HasMany(chapter => chapter.Notes).WithOne().HasForeignKey(note => note.ChapterId);
    }

    public override ValueTask DisposeAsync()
    {
        DisposeCount++;
        TrackedAtDisposal = ChangeTracker.Entries().Count();
        return base.DisposeAsync();
    }
}
