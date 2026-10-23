using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedReconciliationTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReconciliationGenerationTests
{
    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void ParentAndChildMappedSettersCompileInEveryLayout(RepositoryOutputLayout layout)
    {
        using var context = Context();
        var builder = Builder();
        var bundle = RepositoryGenerator.Generate(context, builder.Build(), layout);
        NativeSavedTestHarness.CompileProduction(context, builder, layout);
        ScriptedTestDoubleTestHarness.Compile(bundle);
        var source = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Repositories).Select(file => file.Contents));
        source.Should().Contain("context.Entry(child).Property<global::System.Int32>(\"audit\").CurrentValue");
        source.Should().NotContain("child.Category =").And.NotContain("context.Entry(entity).Property<global::System.Int32>(\"audit\")");
        bundle.Files.Single(file => file.Kind == RepositoryOutputKind.Manifest).Contents.Should()
            .Contain("mappedReconciliationUpdateTargets").And.Contain("SeparateParentAndPerCollectionChildAllowWrite");
        var fixture = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.TestDoubles).Select(file => file.Contents));
        fixture.Should().NotContain("context.Entry(");
        var contracts = string.Join("\n", bundle.Files.Where(file => file.Kind == RepositoryOutputKind.Contracts)
            .SelectMany(file => file.Contents.Split('\n')).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        contracts.Should().NotContain("EntityFrameworkCore").And.NotContain("MappedReconciliationContext");
    }

    [Theory]
    [InlineData(RepositoryOutputLayout.SingleFile)]
    [InlineData(RepositoryOutputLayout.PerEntity)]
    [InlineData(RepositoryOutputLayout.PerContract)]
    public void SqlServerOutputCompilesWithoutConnecting(RepositoryOutputLayout layout)
    {
        using var context = new MappedReconciliationContext(new DbContextOptionsBuilder<MappedReconciliationContext>().UseSqlServer("Server=unused;Database=unused").Options);
        NativeSavedTestHarness.CompileProduction(context, Builder(), layout);
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }

    [Fact]
    public void FixtureOptOutDoesNotChangeProductionOrContracts()
    {
        using var context = Context();
        var enabled = RepositoryGenerator.Generate(context, Builder().Build());
        var disabled = RepositoryGenerator.Generate(context, Builder(backend: false).Build());
        foreach (var file in enabled.Files.Where(file => file.Kind is RepositoryOutputKind.Repositories or RepositoryOutputKind.Contracts))
            disabled.Files.Single(other => other.Kind == file.Kind && other.Name == file.Name).Contents.Should().Be(file.Contents);
        enabled.CreateApiSnapshot().ToJson().Should().Be(disabled.CreateApiSnapshot().ToJson());
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("child")]
    [InlineData("command")]
    public void EachMappedParticipantAndCommandNeedsIndependentFixtureConsent(string missing)
    {
        using var context = Context();
        Action generate = () => RepositoryGenerator.Generate(context, Builder(parentState: missing != "parent", childState: missing != "child", simulate: missing != "command").Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD3402:*");
        var model = RepositoryModelFactory.Create(context, typeof(MappedReconciliationContext).Assembly,
            design: Builder(parentState: missing != "parent", childState: missing != "child", simulate: missing != "command", strict: false).Build());
        model.Entities.Single(entity => entity.EntityName == nameof(MappedCollectionParent)).Commands.Single(command => command.MethodName == "EditAsync")
            .InMemoryCommand!.Supported.Should().BeFalse();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("boxed")]
    [InlineData("widened")]
    [InlineData("nullable")]
    [InlineData("key")]
    [InlineData("owner")]
    [InlineData("matching")]
    [InlineData("permission")]
    public void NamedChildTargetsRetainExactTypesAndIdentityGuards(string mode)
    {
        using var context = Context();
        var builder = Invalid(setters =>
        {
            switch (mode)
            {
                case "unknown": setters.SetProperty<int>("not mapped", 7); break;
                case "boxed": setters.SetProperty<object>(nameof(MappedCollectionChild.Text), child => child.Text); break;
                case "widened": setters.SetProperty<long>("audit", 7); break;
                case "nullable": setters.SetProperty<int?>("audit", 7); break;
                case "key": setters.SetProperty<int>(nameof(MappedCollectionChild.Id), 7); break;
                case "owner": setters.SetProperty<int>(nameof(MappedCollectionChild.ParentId), 7); break;
                case "matching": setters.SetProperty<string>(nameof(MappedCollectionChild.Code), "new"); break;
                default: setters.SetProperty<int>("audit", 7); break;
            }
        }, children =>
        {
            if (mode == "key") children.AllowWrite<int>(nameof(MappedCollectionChild.Id));
            else if (mode == "owner") children.AllowWrite<int>(nameof(MappedCollectionChild.ParentId));
            else if (mode == "matching") children.AllowWrite<string>(nameof(MappedCollectionChild.Code));
            else if (mode == "permission") children.AllowWrite<string>("audit");
        });
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1311:*");
    }

    [Fact]
    public void GeneratedChildColumnsStayExcludedEvenWithPermission()
    {
        using var context = MappedCollectionTestHarness.Context();
        var builder = Invalid(setters => setters.SetProperty<int>("Revision", 7), children => children.AllowWrite<int>("Revision"));
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1311:*");
    }

    [Fact]
    public void ParentPermissionDoesNotGrantChildWritePermission()
    {
        using var context = Context();
        RepositoryCollectionReconciliationBuilder<MappedCollectionChild, MappedCollectionItem>? child = null;
        var builder = BareBuilder();
        builder.Entity<MappedCollectionChild>();
        // Both types have a token with this name. A grant on the root is deliberately insufficient.
        builder.Entity<MappedCollectionParent>().HasCommand("Invalid").Where<MappedReconciliationRequest>((row, request) => row.Id == request.Id)
            .Patch(setters => setters.SetProperty<int>("Revision", 7)).AllowWrite<int>("Revision")
            .ReconcileCollection<MappedCollectionChild, MappedCollectionItem, string, MappedReconciliationRequest>(row => row.Children, request => request.Items,
                row => row.Code, item => item.Code, children => { child = children; children.MaximumItems(3).Update(setters => setters.SetProperty<int>("Revision", 7)); }).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*protected property*");
        child!.AllowWrite<int>("Revision");
        NativeSavedTestHarness.CompileProduction(context, builder);
    }

    [Fact]
    public void ChildProtectionNeedsItsOwnExactTypedGrant()
    {
        using var context = Context();
        RepositoryCollectionReconciliationBuilder<MappedCollectionChild, MappedCollectionItem>? child = null;
        var builder = Invalid(setters => setters.SetProperty<string>(nameof(MappedCollectionChild.Category), "new"), children => child = children);
        builder.Entity<MappedCollectionChild>().ProtectWrite<string>(nameof(MappedCollectionChild.Category));
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*");
        child!.AllowWrite<string>(nameof(MappedCollectionChild.Category));
        NativeSavedTestHarness.CompileProduction(context, builder);
    }

    [Fact]
    public void ChildPermissionDoesNotGrantParentWritePermission()
    {
        using var context = Context();
        var builder = BareBuilder();
        builder.Entity<MappedCollectionChild>();
        var command = builder.Entity<MappedCollectionParent>().HasCommand("Invalid")
            .Patch(setters => setters.SetProperty<int>("Revision", 7))
            .ReconcileCollection<MappedCollectionChild, MappedCollectionItem, string, MappedReconciliationRequest>(row => row.Children, request => request.Items,
                row => row.Code, item => item.Code, children => children.MaximumItems(3)
                    .Update(setters => setters.SetProperty<int>("Revision", 7)).AllowWrite<int>("Revision")).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*protected property*");
        command.AllowWrite<int>("Revision");
        NativeSavedTestHarness.CompileProduction(context, builder);
    }

    [Fact]
    public void NamedParentValueRecipesCannotReadPartiallyLoadedNavigationGraphs()
    {
        using var context = Context();
        var builder = BareBuilder();
        builder.Entity<MappedCollectionChild>();
        builder.Entity<MappedCollectionParent>().HasCommand("Invalid")
            .Patch(setters => setters.SetProperty<int>(nameof(MappedCollectionParent.Counter), row => row.Children.Count))
            .ReconcileCollection<MappedCollectionChild, MappedCollectionItem, string, MappedReconciliationRequest>(row => row.Children, request => request.Items,
                row => row.Code, item => item.Code, children => children.MaximumItems(3).Update(setters => setters.SetProperty<int>("audit", 7))).Save();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1312:*");
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("child")]
    public void ExpectedVersionsStillRequireUnconditionalSetters(string scope)
    {
        using var context = Context();
        var builder = Invalid(setters => setters.SetPropertyWhen<string>(nameof(MappedCollectionChild.Category), (child, item) => item.Text, item => item.Supplied),
            children => { if (scope == "child") children.ExpectVersion<int>("Revision", item => item.Revision); });
        if (scope == "parent")
        {
            // Add a separate invalid command rather than overwriting a registered command.
            builder.Entity<MappedCollectionParent>().HasCommand("ParentInvalid")
                .Patch(setters => setters.SetPropertyWhen<int, MappedReconciliationRequest>("Revision", (row, request) => request.Revision + 1, request => request.IncludeChildren))
                .AllowWrite<int>("Revision").ExpectVersion<int, MappedReconciliationRequest>("Revision", request => request.Revision)
                .ReconcileCollection<MappedCollectionChild, MappedCollectionItem, string, MappedReconciliationRequest>(row => row.Children, request => request.Items,
                    row => row.Code, item => item.Code, children => children.MaximumItems(3).Update(setters => setters.SetProperty<int>("audit", 7))).Save();
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(scope == "parent" ? "RGD2801:*" : "RGD2802:*");
    }

    [Fact]
    public void MixedNamedAndMemberChildTargetsCannotRepeatOneScalar()
    {
        using var context = Context();
        var builder = Invalid(setters => setters.SetProperty(child => child.Text, "one").SetProperty<string>(nameof(MappedCollectionChild.Text), "two"));
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2802:*");
    }

    [Theory]
    [InlineData("dynamic")]
    [InlineData("receiver")]
    [InlineData("type")]
    [InlineData("indexerReceiver")]
    [InlineData("indexerType")]
    public void TrackedChildMappedReadsRequireThisChildAndAnExactLiteralScalar(string mode)
    {
        using var context = Context();
        var builder = Invalid(setters =>
        {
            if (mode == "dynamic") setters.SetProperty<int>("audit", (child, item) => EF.Property<int>(child, item.Text));
            else if (mode == "receiver") setters.SetProperty<int>("audit", child => EF.Property<int>(new MappedCollectionChild(), "audit"));
            else if (mode == "type") setters.SetProperty<int>("audit", child => (int)EF.Property<long>(child, "audit"));
            else if (mode == "indexerReceiver") setters.SetProperty<byte[]?>(MappedReconciliationContext.Stamp, child => (byte[]?)new MappedCollectionChild()[MappedReconciliationContext.Stamp]);
            else setters.SetProperty<int>("audit", child => (int)child[MappedReconciliationContext.Stamp]!);
        }, children => children.AllowWrite<byte[]?>(MappedReconciliationContext.Stamp));
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1312:*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyChildNamesAreRejectedAtDeclaration(string? name)
    {
        Action setter = () => Invalid(setters => setters.SetProperty<int>(name!, 7));
        Action permission = () => Invalid(setters => setters.SetProperty<int>("audit", 7), children => children.AllowWrite<int>(name!));
        setter.Should().Throw<ArgumentException>().WithMessage("RGD1311:*");
        permission.Should().Throw<ArgumentException>().WithMessage("RGD1311:*");
    }

    internal static RepositoryModelBuilder Invalid(Action<RepositoryCollectionUpdateBuilder<MappedCollectionChild, MappedCollectionItem>> update,
        Action<RepositoryCollectionReconciliationBuilder<MappedCollectionChild, MappedCollectionItem>>? configure = null)
    {
        var builder = BareBuilder();
        builder.Entity<MappedCollectionChild>();
        builder.Entity<MappedCollectionParent>().HasCommand("Invalid").Where<MappedReconciliationRequest>((row, request) => row.Id == request.Id)
            .ReconcileCollection<MappedCollectionChild, MappedCollectionItem, string, MappedReconciliationRequest>(row => row.Children, request => request.Items,
                row => row.Code, item => item.Code, children => { children.MaximumItems(3).Update(update); configure?.Invoke(children); }).Save();
        return builder;
    }
}
