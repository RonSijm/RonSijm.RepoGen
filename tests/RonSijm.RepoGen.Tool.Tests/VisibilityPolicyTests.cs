using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class VisibilityPolicyTests
{
    [Fact]
    public async Task PoliciesApplyToReadsWritesAndEveryReplacementRootAndOnlyNamedBypassesRelaxThem()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Owners.AddRange(new CommandOwner { Id = 1 }, new CommandOwner { Id = 2 });
        context.Rows.AddRange(new CommandRow { Id = 1, Name = "visible", OwnerId = 1 }, new CommandRow { Id = 2, Name = "hidden", OwnerId = 2 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseClosedRepositories("Visibility.Contracts").GenerateOnlyConfiguredEntities());
        builder.Entity<CommandOwner>(owner =>
        {
            owner.RepositoryEnabled = false;
            owner.HasVisibilityPolicy("Owners", value => value.Id == 1);
        });
        builder.Entity<CommandRow>(entity =>
        {
            entity.HasVisibilityPolicy("Visible", row => row.Name != "hidden");
            entity.HasQuery("Visible").Select(row => row.Name).List();
            entity.HasQuery("All").BypassVisibility("Visible").Select(row => row.Name).List();
            entity.HasQuery("Join").From(sources => sources.Set<CommandRow>().Join(sources.Set<CommandOwner>(), row => row.OwnerId, owner => owner.Id, (row, owner) => row.Name)).List();
            entity.HasQuery("JoinAll").BypassVisibility("Visible", "Owners")
                .From(sources => sources.Set<CommandRow>().Join(sources.Set<CommandOwner>(), row => row.OwnerId, owner => owner.Id, (row, owner) => row.Name)).List();
            entity.HasCommand("Delete").Where<int>((row, id) => row.Id == id).Delete();
            entity.HasCommand("DeleteHidden").BypassVisibility("Visible").Where<int>((row, id) => row.Id == id).Delete();
        });
        var model = RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
        var generated = ClosedRepositoryTests.Compile(model);
        var operations = Activator.CreateInstance(generated.GetType(typeof(CommandRow).Namespace + ".CommandRowRepositoryOperations")!, context)!;
        (await Call<Task<IReadOnlyList<string>>>(operations, "VisibleAsync", CancellationToken.None)).Should().Equal("visible");
        (await Call<Task<IReadOnlyList<string>>>(operations, "AllAsync", CancellationToken.None)).Should().BeEquivalentTo("visible", "hidden");
        (await Call<Task<IReadOnlyList<string>>>(operations, "JoinAsync", CancellationToken.None)).Should().Equal("visible");
        (await Call<Task<IReadOnlyList<string>>>(operations, "JoinAllAsync", CancellationToken.None)).Should().HaveCount(2);
        (await Call<Task<int>>(operations, "DeleteAsync", 2, CancellationToken.None)).Should().Be(0);
        (await Call<Task<int>>(operations, "DeleteHiddenAsync", 2, CancellationToken.None)).Should().Be(1);
        RepositoryManifestRenderer.Render(model).Should().Contain("bypassedVisibility").And.Contain("Owners");
        ClosedRepositorySourceRenderer.RenderContracts(model).Should().NotContain("BypassVisibility").And.NotContain("GetAllForQuery");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingAndDuplicatePoliciesAreDiagnosed(bool duplicate)
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Entity<CommandRow>(entity =>
        {
            entity.HasVisibilityPolicy("Visible", row => row.Id > 0);
            if (duplicate)
            {
                entity.HasVisibilityPolicy("Visible", row => row.Id < 10);
            }
            else
            {
                entity.HasQuery("Invalid").BypassVisibility("Missing").Count();
            }
        });
        Action generate = () => RepositoryModelFactory.Create(context, typeof(CommandRow).Assembly, true, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(duplicate ? "*Duplicate visibility*" : "*unknown visibility*");
    }

    private static T Call<T>(object instance, string name, params object[] arguments) => (T)instance.GetType().GetMethod(name)!.Invoke(instance, arguments)!;
}