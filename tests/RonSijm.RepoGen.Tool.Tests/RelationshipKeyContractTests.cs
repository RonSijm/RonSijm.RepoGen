using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.RelationshipKeyTestHarness;
using static RonSijm.RepoGen.Tool.Tests.ManyReadTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class RelationshipKeyContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrdinaryCompositeQueriesMatchExplicitSqliteCollationForSelectedAsciiRelationships(bool mapped)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = NavigationContext(new DbContextOptionsBuilder<NavigationReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var libraries = NavigationReadTestHarness.Libraries(); foreach (var row in libraries) row.Books = [];
        context.AddRange(libraries); context.AddRange(new NavigationReadAuthor { Id = 1, Name = "Ada" }, new NavigationReadAuthor { Id = 2, Name = "Bob" });
        context.AddRange(Books()); context.Add(new NavigationReadProfile { Id = 1, LibraryId = 1, Caption = "Main" });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, NavigationBuilder(mapped).Build());
        const string ns = "Generated.RelationshipKeys.Persistence.";
        var realLibrary = Activator.CreateInstance(assembly.GetType(ns + nameof(NavigationReadLibrary) + "RepositoryOperations")!, context)!;
        var realBook = Activator.CreateInstance(assembly.GetType(ns + nameof(NavigationReadBook) + "RepositoryOperations")!, context)!;
        var (library, book, _) = NavigationRepositories(mapped);
        async Task Equal<T>(object real, object fake, string name, params object?[] arguments)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(fake, name, arguments)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, name, arguments)), name);
        await Equal<IReadOnlyList<NavigationReadCard>>(realLibrary, library, "CardsAsync");
        await Equal<IReadOnlyList<string>>(realBook, book, "ReferencesAsync");
        await Equal<IReadOnlyList<string>>(realBook, book, "SourceAsync");
        await Equal<NavigationReadDetail?>(realLibrary, library, "DetailAsync", 1);
        await Equal<PagedResult<List<string>>>(realLibrary, library, "NestedAsync", 2, 1);
        // Equality expressions themselves are deliberately not rewritten to database collation.
        (await Invoke<Task<IReadOnlyList<int>>>(library, "CodesAsync", "SAME")).Should().BeEmpty();
        (await Invoke<Task<IReadOnlyList<int>>>(realLibrary, "CodesAsync", "SAME")).Should().Equal(1, 2);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task BothJoinDirectionsPagesAndFreshSavedResultsMatchConfiguredSqliteRelationships(bool mapped, bool optional)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = ManyContext(optional, new DbContextOptionsBuilder<ManyReadContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); context.AddRange(Owners()); context.AddRange(Tags());
        await context.SaveChangesAsync(); context.AddRange(Links(optional));
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var assembly = SavedResultTestHarness.CompileProduction(context, ManyBuilder(mapped, optional).Build());
        const string ns = "Generated.ManyRead.Persistence.";
        var realOwner = Activator.CreateInstance(assembly.GetType(ns + "ManyReadOwnerRepositoryOperations")!, context)!;
        var realTag = Activator.CreateInstance(assembly.GetType(ns + "ManyReadTagRepositoryOperations")!, context)!;
        var (owner, tag, _) = ManyRepositories(mapped, optional);
        IReadOnlyList<ManyReadPatch> input = [new(2, "second"), new(1, "first")];
        async Task Equal<T>(object real, object fake, string name, params object?[] arguments)
            => JsonSerializer.Serialize(await Invoke<Task<T>>(fake, name, arguments)).Should().Be(JsonSerializer.Serialize(await Invoke<Task<T>>(real, name, arguments)), name);
        await Equal<IReadOnlyList<ManyReadTagCard>>(realTag, tag, "OwnersAsync");
        if (optional)
        {
            await Equal<IReadOnlyList<NullTargetReadCard>>(realOwner, owner, "CardsAsync");
            await Equal<IReadOnlyList<int>>(realOwner, owner, "HasMissingAsync");
            await Equal<PagedResult<NullTargetReadCard>>(realOwner, owner, "PageAsync", 2, 1);
            await Equal<CommandResult<NullTargetReadCard>>(realOwner, owner, "RenameAsync", 1);
            await Equal<CommandResult<IReadOnlyList<NullTargetReadCard>>>(realOwner, owner, "RenameManyAsync", input);
        }
        else
        {
            await Equal<IReadOnlyList<ManyReadCard>>(realOwner, owner, "CardsAsync");
            await Equal<IReadOnlyList<int>>(realOwner, owner, "SourceAsync");
            await Equal<PagedResult<List<string>>>(realOwner, owner, "PageAsync", 2, 1);
            await Equal<CommandResult<ManyReadCard>>(realOwner, owner, "RenameAsync", 1);
            await Equal<CommandResult<IReadOnlyList<ManyReadCard>>>(realOwner, owner, "RenameManyAsync", input);
        }
        context.Set<ManyReadLink>().Count().Should().Be(optional ? 6 : 4);
    }
}
