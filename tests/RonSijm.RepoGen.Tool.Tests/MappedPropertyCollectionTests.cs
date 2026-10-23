using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedPropertyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyCollectionTests
{
    [Fact]
    public async Task ChildReconciliationChecksShadowOriginalVersionsAndRetainsOwnerScope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new MappedPropertyCollectionContext(new DbContextOptionsBuilder<MappedPropertyCollectionContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.AddRange(new MappedPropertyOwner { Id = 1 }, new MappedPropertyOwner { Id = 2 });
        context.AddRange(new MappedPropertyChild { Id = 1, OwnerId = 1 }, new MappedPropertyChild { Id = 2, OwnerId = 1 }, new MappedPropertyChild { Id = 3, OwnerId = 2 });
        await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var builder = Builder(); builder.Entity<MappedPropertyChild>();
        builder.Entity<MappedPropertyOwner>().HasCommand("Change").Where<MappedPropertyCollectionInput>((row, input) => row.Id == input.Id)
            .Patch(setters => setters.SetProperty(row => row.Title, "saved"))
            .ReconcileCollection<MappedPropertyChild, MappedPropertyItem, int, MappedPropertyCollectionInput>(row => row.Children, input => input.Items,
                row => row.Id, item => item.Id, children => children.MaximumItems(10)
                    .Update(setters => setters.SetProperty(row => row.Title, (row, item) => item.Title).SetProperty(row => row.Counter, row => row.Counter + 1))
                    .ExpectVersion<int>(MappedPropertyContext.Token, item => item.Version)).Save();
        var assembly = ImmediateTestHarness.CompileProduction(context, builder);
        var operations = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedPropertyOwnerRepositoryOperations")!, context)!;
        async Task<CommandResult> Call(params MappedPropertyItem[] items) => await ReturningWriteTests.Call<Task<CommandResult>>(operations, "ChangeAsync", new MappedPropertyCollectionInput(1, items), CancellationToken.None);
        (await Call(new(1, "first", 1), new(2, "second", 99))).Status.Should().Be(CommandStatus.Conflict);
        (await context.Children.AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Counter == 0);
        (await Call(new(1, "first", 1), new(2, "second", 1))).Status.Should().Be(CommandStatus.Applied);
        (await context.Children.AsNoTracking().Where(row => row.OwnerId == 1).Select(row => EF.Property<int>(row, MappedPropertyContext.Token)).ToListAsync()).Should().Equal(2, 2);
        (await context.Children.AsNoTracking().SingleAsync(row => row.Id == 3)).Counter.Should().Be(0);
    }
}
