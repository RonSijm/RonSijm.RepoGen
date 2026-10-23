using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class PagingPolicyTests
{
    [Fact]
    public async Task CountedPagesUseOneStableReadTransactionAndResultCursorsSeekOnTheServer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var recorder = new Recorder();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).AddInterceptors(recorder).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        context.Rows.AddRange(new CommandRow { Name = "one", OwnerId = 1 }, new CommandRow { Name = "two", OwnerId = 1 }, new CommandRow { Name = "three", OwnerId = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Paging.Contracts").UseCommandTimeout(5).GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>(entity =>
        {
            entity.HasQuery("Page").Select(row => new SqlNameRow { Id = row.Id, Name = row.Name }).OrderBy(row => row.Id).RequirePagingOrder().Paged(RepositoryPageConsistency.Serializable);
            entity.HasQuery("Cursor").Select(row => new SqlNameRow { Id = row.Id, Name = row.Name }).OrderBy(row => row.Id).CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
        });
        var assembly = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build()));
        var operations = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, context)!;
        context.Database.SetCommandTimeout(7);
        recorder.Commands.Clear();
        var page = await Call<Task<PagedResult<SqlNameRow>>>(operations, "PageAsync", 1, 2, CancellationToken.None);
        page.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(2);
        recorder.Commands.Should().HaveCount(2).And.OnlyContain(command => command.Transaction && command.Timeout == 5);
        context.Database.CurrentTransaction.Should().BeNull();
        context.Database.GetCommandTimeout().Should().Be(7);
        var first = await Call<Task<CursorPagedResult<SqlNameRow, int>>>(operations, "CursorAsync", new CursorPageRequest<int>(2), CancellationToken.None);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().Be(2);
        recorder.Commands.Clear();
        var second = await Call<Task<CursorPagedResult<SqlNameRow, int>>>(operations, "CursorAsync", new CursorPageRequest<int>(first.NextCursor, 2), CancellationToken.None);
        second.HasMore.Should().BeFalse();
        second.Items.Should().ContainSingle().Which.Id.Should().Be(3);
        recorder.Commands.Should().ContainSingle().Which.Sql.Should().Contain("WHERE").And.Contain("LIMIT");
        context.Database.GetCommandTimeout().Should().Be(7);
    }

    [Fact]
    public void GuardedUnorderedPagingIsRejected()
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Paging.Contracts").GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>().HasQuery("Invalid").Select(row => row.Name).RequirePagingOrder().Paged();
        Action generate = () => RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*explicit paging OrderBy*");
    }

    private static T Call<T>(object instance, string name, params object[] arguments) => (T)instance.GetType().GetMethod(name)!.Invoke(instance, arguments)!;

    [Fact]
    public void AResultCursorCannotBeAppliedToAChangedResultShape()
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Paging.Contracts").GenerateOnlyConfiguredEntities());
        var results = builder.Entity<CommandRow>().HasQuery("Invalid").Select(row => new SqlNameRow { Id = row.Id, Name = row.Name }).OrderBy(row => row.Id);
        results.CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
        results.Select(row => row.Name);
        Action generate = () => RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*final result shape*");
    }

    private sealed class Recorder : DbCommandInterceptor
    {
        public List<(bool Transaction, int Timeout, string Sql)> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add((command.Transaction is not null, command.CommandTimeout, command.CommandText));
            return ValueTask.FromResult(result);
        }
    }
}