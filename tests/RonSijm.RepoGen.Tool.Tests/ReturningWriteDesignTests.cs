using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using RonSijm.RepoGen.SqlServer;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningWriteDesignTests
{
    [Theory]
    [InlineData("missingWriter", "RGD1305")]
    [InlineData("invalidWriter", "RGD1305")]
    [InlineData("wrongProvider", "RGD1302")]
    [InlineData("notClosed", "RGD1305")]
    [InlineData("stage", "RGD1306")]
    [InlineData("save", "RGD1306")]
    [InlineData("reload", "RGD1306")]
    [InlineData("outcome", "RGD1306")]
    [InlineData("tracked", "RGD1306")]
    [InlineData("deleteWithSetters", "RGD1306")]
    [InlineData("entityHelper", "RGD1306")]
    public void UnsupportedConfigurationFailsWithAStableDiagnostic(string scenario, string code)
    {
        using var context = new ReturningContext(new DbContextOptionsBuilder<ReturningContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options =>
        {
            options.UseSimpleDefaults().GenerateOnlyConfiguredEntities();
            if (scenario != "notClosed")
            {
                options.UseClosedRepositories("Returning.Contracts");
            }

            if (scenario is not "missingWriter" and not "invalidWriter" and not "wrongProvider")
            {
                options.UseReturningWrites<SqliteReturningWriter>();
            }

            if (scenario == "invalidWriter")
            {
                options.UseReturningWrites<string>();
            }

            if (scenario == "wrongProvider")
            {
                options.UseReturningWrites<SqlServerReturningWriter>();
            }
        });
        var command = builder.Entity<ReturningRow>().HasCommand("Change").Update(setters => setters.SetProperty(row => row.Quantity, row => row.Quantity + 1)).ReturnsMany(row => row.Quantity);
        switch (scenario)
        {
            case "stage": command.Stage(); break;
            case "save": command.Save(); break;
            case "reload": command.ReloadAfterSave(); break;
            case "outcome": command.WithOutcome(); break;
            case "tracked": command.TrackedDelete().Save(); break;
            case "deleteWithSetters": command.Delete(); break;
            case "entityHelper": command.ReturnsMany(row => ReturningDesignMapping.FromEntity(row)); break;
        }

        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(code + ":");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData("shadow")]
    [InlineData("trigger")]
    [InlineData("schema")]
    public void UnsupportedTableMappingsAreRejectedBeforeSqlOrFiles(string shape)
    {
        using var context = new ReturningContext(new DbContextOptionsBuilder<ReturningContext>().UseSqlite("Data Source=:memory:").Options, shape);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Returning.Contracts")
            .UseReturningWrites<SqliteReturningWriter>().GenerateOnlyConfiguredEntities());
        builder.Entity<ReturningRow>().HasCommand("Delete").Delete().ReturnsMany(row => row.Id);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith("RGD1306:");
    }

    [Theory]
    [InlineData("key")]
    [InlineData("computed")]
    [InlineData("protected")]
    [InlineData("conditional")]
    [InlineData("navigation")]
    public void UnsafePropertyWritesAndNavigationResultsAreNotInferred(string scenario)
    {
        using var context = new CommandContext(new DbContextOptionsBuilder<CommandContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Returning.Contracts")
            .UseReturningWrites<SqliteReturningWriter>().GenerateOnlyConfiguredEntities());
        var command = builder.Entity<CommandRow>().HasCommand("Change");
        switch (scenario)
        {
            case "key": command.Update(setters => setters.SetProperty(row => row.Id, 4)).AllowWrite(row => row.Id); break;
            case "computed": command.Update(setters => setters.SetProperty(row => row.Normalized, "no")).AllowWrite(row => row.Normalized); break;
            case "protected": command.Update(setters => setters.SetProperty(row => row.OwnerId, 4)); break;
            case "conditional": command.Update(setters => setters.SetPropertyWhen<string?, ReturningRequest>(row => row.Description, (row, request) => request.Prefix, request => request.Increment > 0)); break;
            case "navigation": command.Delete(); break;
        }

        if (scenario == "navigation")
        {
            command.ReturnsMany(row => row.Owner!.Id);
        }
        else
        {
            command.ReturnsMany(row => row.Id);
        }

        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnablingAnUnusedCapabilityAddsNoConstructorDependency(bool disabledAgain)
    {
        using var context = new ReturningContext(new DbContextOptionsBuilder<ReturningContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Returning.Contracts")
            .UseReturningWrites<SqliteReturningWriter>().GenerateOnlyConfiguredEntities());
        if (disabledAgain)
        {
            builder.Configure(options => options.UseReturningWrites<SqliteReturningWriter>(false));
        }

        builder.Entity<ReturningRow>().HasQuery("Ids").Select(row => row.Id).List();
        var model = RepositoryModelFactory.Create(context, typeof(ReturningRow).Assembly, true, builder.Build());
        RepositorySourceRenderer.Render(model).Should().NotContain("__repoGenReturningWriter").And.NotContain("ReturningWriteExecutor");
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
    }

    [Fact]
    public void MutationResultCopiesItsInputAndReportsOnlyItsReturnedRows()
    {
        var source = new List<int> { 1, 2 };
        var result = new MutationResult<int>(source);
        source.Clear();
        result.AffectedRows.Should().Be(2);
        result.Items.Should().Equal(1, 2);
        Action modify = () => ((IList<int>)result.Items).Add(3);
        modify.Should().Throw<NotSupportedException>();
    }
}

public static class ReturningDesignMapping
{
    public static int FromEntity(ReturningRow row) => row.Id;
}