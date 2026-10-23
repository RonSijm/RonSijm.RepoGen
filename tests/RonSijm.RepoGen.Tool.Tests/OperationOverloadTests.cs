using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Caching;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class OperationOverloadTests
{
    [Fact]
    public async Task OverloadsBindExactPortsKeepUnboundMethodsAndHaveIndependentVisibilityAndCacheShapes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.Add(new CommandOwner { Id = 1 });
        context.Rows.AddRange(new CommandRow { Id = 1, Name = "visible", OwnerId = 1 }, new CommandRow { Id = 2, Name = "hidden", OwnerId = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Overload.Contracts")
            .UseClosedCaching("overload-fixture").GenerateOnlyConfiguredEntities());
        builder.Entity<CommandRow>(entity =>
        {
            entity.HasVisibilityPolicy("Visible", row => row.Name != "hidden");
            entity.BindReadPort<IOverloadedNames>();
            entity.BindWritePort<IOverloadedWrites>();
            entity.HasQuery("Find").Cache().Where<int>((row, id) => row.Id == id).Select(row => row.Name).SingleOrDefault();
            entity.HasQuery("Find").Cache().AllowNullInput<string>("term").BypassVisibility("Visible")
                .Where<string>((row, term) => term == null || row.Name.Contains(term)).OrderBy(row => row.Id).Select(row => row.Name).List();
            entity.HasQuery("Find").Where<long>((row, minimum) => row.Id >= minimum).Count();
            entity.HasQuery("After").Select(row => new SqlNameRow { Id = row.Id, Name = row.Name }).OrderBy(row => row.Id)
                .CursorPaged<int>((row, cursor) => row.Id > cursor, row => row.Id);
            entity.HasCommand("Rename").Where<int>((row, id) => row.Id == id)
                .Update(setters => setters.SetProperty<string, string>(row => row.Name, (row, name) => name));
            entity.HasCommand("Rename").Where<OverloadRename>((row, request) => row.Id == request.Id)
                .Update(setters => setters.SetProperty<string, OverloadRename>(row => row.Name, (row, request) => request.Name));
            entity.HasCommand("SetText").AllowNullInput<string>("text").Where<int>((row, id) => row.Id == id)
                .Update(setters => setters.SetProperty<string?, string?>(row => row.Description, (row, text) => text));
        });
        var model = RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, true, builder.Build());
        var assembly = ClosedRepositoryTests.Compile(model);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var operations = Activator.CreateInstance(assembly.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, context, new MemoryRepositoryResultCache(memory))!;
        var read = (IOverloadedNames)operations;
        var write = (IOverloadedWrites)operations;
        (await read.FindAsync(2)).Should().BeNull();
        (await read.FindAsync(null)).Should().Equal("visible", "hidden");
        (await read.FindAsync(1)).Should().Be("visible");
        (await read.FindAsync(null)).Should().HaveCount(2);
        (await write.RenameAsync(1, "updated")).Should().Be(1);
        (await read.FindAsync(1)).Should().Be("updated");
        (await write.RenameAsync(new OverloadRename(1, "request"))).Should().Be(1);
        (await read.FindAsync(1)).Should().Be("request");
        (await read.AfterAsync(new CursorPageRequest<int>(10))).Items.Should().ContainSingle().Which.Name.Should().Be("request");
        (await write.SetTextAsync(1, null)).Should().Be(1);
        (await context.Rows.AsNoTracking().SingleAsync(row => row.Id == 1)).Description.Should().BeNull();
        var contracts = ClosedRepositorySourceRenderer.RenderContracts(model);
        contracts.Should().Contain("IOverloadedNames").And.Contain("global::System.Int64 minimum");
        var count = (Task<int>)operations.GetType().GetMethod("FindAsync", [typeof(long), typeof(CancellationToken)])!.Invoke(operations, [1L, CancellationToken.None])!;
        (await count).Should().Be(1);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("nullabilityOnly")]
    [InlineData("missing")]
    [InlineData("wrongType")]
    public void InvalidOverloadAndNullabilityConfigurationsHaveDiagnostics(string scenario)
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Overload.Contracts").GenerateOnlyConfiguredEntities());
        var entity = builder.Entity<CommandRow>();
        entity.HasQuery("Find").Where<string>((row, term) => row.Name.Contains(term)).Select(row => row.Name).List();
        if (scenario is "duplicate" or "nullabilityOnly")
        {
            var second = entity.HasQuery("Find").Where<string>((row, term) => row.Name == term).Select(row => row.Id).List();
            if (scenario == "nullabilityOnly")
            {
                second.AllowNullInput<string>("term");
            }
        }

        if (scenario == "missing")
        {
            entity.HasQuery("Invalid").AllowNullInput<string>("absent").Select(row => row.Name).List();
        }

        if (scenario == "wrongType")
        {
            entity.HasQuery("Invalid").AllowNullInput<string>("number").Where<int>((row, number) => row.Id == number).Select(row => row.Name).List();
        }

        Action generate = () => RepositoryModelFactory.Create(context, typeof(CommandContext).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(scenario is "duplicate" or "nullabilityOnly" ? "*Duplicate closed operation*" : "*nullable input*");
    }
}

public interface IOverloadedNames
{
    Task<string?> FindAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> FindAsync(string? term, CancellationToken cancellationToken = default);
    Task<CursorPagedResult<SqlNameRow, int>> AfterAsync(CursorPageRequest<int> cursorPage, CancellationToken cancellationToken = default);
}

public interface IOverloadedWrites
{
    Task<int> RenameAsync(int id, string name, CancellationToken cancellationToken = default);
    Task<int> RenameAsync(OverloadRename request, CancellationToken cancellationToken = default);
    Task<int> SetTextAsync(int id, string? text, CancellationToken cancellationToken = default);
}

public sealed record OverloadRename(int Id, string Name);