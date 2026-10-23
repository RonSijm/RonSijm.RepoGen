using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Sqlite;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.ReturningMappedTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningMappedDesignTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANewExplicitPermissionIsRequiredAndCanBeRemoved(bool shadow)
    {
        using var context = Empty(); var builder = Builder(mapped: false, shadow: shadow);
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*");
        builder = Minimal(); builder.Entity<ReturningMappedRow>().HasCommand("Remove").Delete().ReturnsMany(row => row.Id)
            .IncludeShadowProperties(shadow).IncludeMappedProperties().IncludeMappedProperties(false);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*");
        Action narrow = () => RelationalReturningWriter.ValidateModel(context.Model.FindEntityType(typeof(ReturningMappedRow))!, includeShadowProperties: true);
        narrow.Should().Throw<NotSupportedException>();
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Theory]
    [InlineData("readonly")]
    [InlineData("fieldKey")]
    [InlineData("indexerKey")]
    [InlineData("trigger")]
    public void CaptureDoesNotRelaxImmutableFieldsClrKeysOrTableBoundaries(string shape)
    {
        using var context = Empty(shape); var builder = Minimal();
        builder.Entity<ReturningMappedRow>().HasCommand("Remove").Delete().ReturnsMany(row => row.Id).IncludeMappedProperties();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*");
        Action runtime = () => RelationalReturningWriter.ValidateModel(context.Model.FindEntityType(typeof(ReturningMappedRow))!, includeMappedProperties: true);
        runtime.Should().Throw<Exception>();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("wrongType")]
    [InlineData("dynamic")]
    [InlineData("receiver")]
    [InlineData("entity")]
    [InlineData("indexer")]
    public void ReturnedReadsNeedExactMappedScalarsAndFixedNames(string shape)
    {
        using var context = Empty(); var builder = Minimal(); var command = builder.Entity<ReturningMappedRow>().HasCommand("Remove").Delete().IncludeMappedProperties();
        switch (shape)
        {
            case "unknown": command.ReturnsMany(row => EF.Property<int>(row, "Unknown")); break;
            case "wrongType": command.ReturnsMany(row => EF.Property<long>(row, "audit")); break;
            case "dynamic": command.ReturnsMany<int, string>((row, name) => EF.Property<int>(row, name)); break;
            case "receiver": command.ReturnsMany(row => EF.Property<int>(new ReturningMappedRow(), "audit")); break;
            case "entity": command.ReturnsMany(row => row); break;
            case "indexer": command.ReturnsMany(row => row[ReturningMappedContext.Note]); break;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(shape == "entity" ? "RGD1502:*" : "RGD1306:*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThirdPartyWritersMustAdvertiseMappedMaterializationSeparately(bool state)
    {
        using var context = Empty(); var builder = Minimal();
        if (state) builder.Configure(options => options.UseReturningWrites<ReturningShadowOnlyWriter>());
        else builder.Configure(options => options.UseReturningWrites<ReturningLegacyWriter>());
        builder.Entity<ReturningMappedRow>().HasCommand("Remove").Delete().ReturnsMany(row => row.Id).IncludeMappedProperties();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1308:*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyNativeReturningCommandsCanRequestMappedCapture(bool tracked)
    {
        using var context = Empty(); var builder = Minimal(); var command = builder.Entity<ReturningMappedRow>().HasCommand("Remove");
        if (tracked) command.TrackedDelete().Stage(); else command.Delete();
        command.IncludeMappedProperties();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1306:*only*native ReturnsMany*");
    }

    [Fact]
    public void ReceiptsDescribeAllMappedKindsWithoutChangingShadowOnlyReceipts()
    {
        using var context = Empty(); var bundle = RepositoryGenerator.Generate(context, Builder().Build());
        using var manifest = JsonDocument.Parse(bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents);
        var values = manifest.RootElement.GetProperty("entities")[0].GetProperty("commands")[0].GetProperty("returningPropertyValues");
        values.GetProperty("permission").GetString().Should().Be("PerCommandIncludeMappedProperties");
        values.GetProperty("properties").EnumerateArray().Select(property => property.GetProperty("accessKind").GetString())
            .Should().Contain(["FieldOnly", "FieldBacked", "Indexer", "Shadow"]);
        values.GetProperty("memberReads").GetString().Should().Be("MappedScalarBufferNotEntityGetterBehavior");
        using var ordinary = ReturningShadowTestHarness.Empty();
        RepositoryGenerator.Generate(ordinary, ReturningShadowTestHarness.Builder().Build()).Files.Single(file => file.Kind == RepositoryOutputKind.Manifest)
            .Contents.Should().NotContain("accessKind").And.NotContain("IncludeMappedProperties");
    }

    internal static RepositoryModelBuilder Minimal() => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
        .UseClosedRepositories(Namespace + ".Contracts").UseGeneratedNamespace(Namespace + ".Persistence").UseReturningWrites<SqliteReturningWriter>());
}
