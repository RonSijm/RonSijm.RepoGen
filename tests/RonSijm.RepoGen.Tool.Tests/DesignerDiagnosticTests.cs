using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.SqlServer;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class DesignerDiagnosticTests
{
    [Theory]
    [InlineData("missingProjection", "RGD1001")]
    [InlineData("ambiguousProjection", "RGD1002")]
    [InlineData("duplicateProfile", "RGD1003")]
    [InlineData("duplicateOperation", "RGD1004")]
    [InlineData("disabledPort", "RGD1101")]
    [InlineData("missingOperation", "RGD1103")]
    [InlineData("portType", "RGD1104")]
    [InlineData("unsafeLease", "RGD1201")]
    [InlineData("missingKey", "RGD1202")]
    [InlineData("unsafeCache", "RGD1203")]
    [InlineData("providerMismatch", "RGD1302")]
    [InlineData("missingProfile", "RGD1404")]
    [InlineData("unorderedPage", "RGD1501")]
    [InlineData("entityLeak", "RGD1502")]
    public void CommonDesignFailuresHaveStablePrefixesAndStillFailBeforeAnyOutputs(string scenario, string code)
    {
        using var context = new ClosedContext(new DbContextOptionsBuilder<ClosedContext>().UseSqlite("Data Source=:memory:").Options);
        var builder = new RepositoryModelBuilder();
        if (scenario == "unsafeLease")
        {
            builder.Configure(options => options.UseAsyncContextLeases());
        }
        else
        {
            builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Diagnostic.Contracts").GenerateOnlyConfiguredEntities());
        }

        var entity = builder.Entity<ClosedRow>();
        entity.WriteEnabled = false;
        switch (scenario)
        {
            case "missingProjection": entity.HasQuery("Read").ProjectTo<ClosedRowDto>().List(); break;
            case "ambiguousProjection":
                entity.HasProjection("ProjectToOne", row => new ClosedRowDto(row.Id, row.Name));
                entity.HasProjection("ProjectToTwo", row => new ClosedRowDto(row.Id, row.Name));
                entity.HasQuery("Read").ProjectTo<ClosedRowDto>().List();
                break;
            case "duplicateProfile":
                entity.HasLoadProfile("Same", _ => { });
                entity.HasLoadProfile("Same", _ => { });
                break;
            case "duplicateOperation":
                entity.HasQuery("Count").Count();
                entity.HasQuery("Count").Count();
                break;
            case "disabledPort":
                entity.ReadEnabled = false;
                entity.BindReadPort<IDiagnosticPort>();
                break;
            case "missingOperation": entity.BindReadPort<IDiagnosticPort>(); break;
            case "portType":
                entity.BindReadPort<IDiagnosticPort>();
                entity.HasQuery("Count").Exists();
                break;
            case "missingKey":
                builder.Configure(options => options.UseKeyedContextLeases<string>());
                entity.HasQuery("Count").Count();
                break;
            case "unsafeCache": builder.Configure(options => options.UseClosedCaching()); break;
            case "providerMismatch": builder.Configure(options => options.UseAtomicWrites<SqlServerAtomicWriter>()); break;
            case "missingProfile": entity.HasQuery("Read").UseLoadProfile("Absent").Count(); break;
            case "unorderedPage": entity.HasQuery("Page").Select(row => row.Id).RequirePagingOrder().Paged(); break;
            case "entityLeak": entity.HasQuery("Read").List(); break;
        }

        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(code + ":");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}

public interface IDiagnosticPort
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}