// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/SimpleRepositoryDesigner.cs and README.md.
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Models.Basic;

namespace RonSijm.RepoGen.ConfigurationExamples.Simple;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        // No designer, cache, DI registration, tenant selection, or unit-of-work service is needed.
        await using var context = new NotesContext(new DbContextOptionsBuilder<NotesContext>().UseSqlite("Data Source=:memory:").Options);
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        var repository = new NoteRepository(context);

        repository.Add(new Note { Title = "Try the simplest setup" });
        await repository.SaveChangesAsync();
        var note = await repository.ByTitle("Try the simplest setup").AsEntityAsync();
        if (note is null)
        {
            throw new InvalidOperationException("The generated unique lookup did not return the saved note.");
        }

        var notes = await repository.GetAll().AsEntitiesAsync();
        if (notes.Count != 1)
        {
            throw new InvalidOperationException("Unexpected note count.");
        }

        if (!await repository.ById(note.Id).Delete().SaveChangesAsync())
        {
            throw new InvalidOperationException("The fluent delete did not save.");
        }

        if (await repository.GetAll().CountAsync() != 0)
        {
            throw new InvalidOperationException("The note was not deleted.");
        }

        Console.WriteLine("Simple: generated key/index lookups and CRUD passed; direct DbContext, no optional adapters.");
    }
}
