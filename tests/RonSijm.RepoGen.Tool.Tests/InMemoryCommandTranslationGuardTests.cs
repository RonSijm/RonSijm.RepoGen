using FluentAssertions;
using RonSijm.RepoGen.Design;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryCommandTranslationGuardTests
{
    [Theory]
    [InlineData("protected")]
    [InlineData("key")]
    [InlineData("reload")]
    [InlineData("script")]
    [InlineData("immediate")]
    public void TranslationsCannotRelaxWritePermissionsKeysGraphOrProviderPolicies(string kind)
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = InMemoryCommandTranslationTestHarness.Builder(); var entity = builder.Entity<MemoryWriteNote>();
        var command = entity.HasCommand("Unsafe").Where<int>((row, id) => row.Id == TranslationCalls.Echo(id)).UseInMemoryTranslations();
        switch (kind)
        {
            case "protected": command.Patch(setters => setters.SetProperty(row => row.Version, row => TranslationCalls.Shift(row.Version, 1))).Save(); break;
            case "key": command.Patch(setters => setters.SetProperty(row => row.Id, row => TranslationCalls.Shift(row.Id, 1))).AllowWrite(row => row.Id).Save(); break;
            case "reload": command.Patch(setters => setters.SetProperty(row => row.Title, "changed")).Save().ReloadAfterSave(); break;
            case "script": command.Patch(setters => setters.SetProperty(row => row.Title, "changed")).Save().RequireTestOverride("Requires an explicit script despite translations."); break;
            case "immediate": command.Update(setters => setters.SetProperty(row => row.Title, "changed")); break;
        }
        if (kind == "script")
        {
            var model = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: builder.Build());
            model.Entities.Single().Commands.Single(operation => operation.MethodName == "UnsafeAsync").InMemoryCommand!.Supported.Should().BeFalse();
            return;
        }
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage(kind switch
        {
            "protected" => "*AllowWrite*", "key" => "*immutable key*", "reload" => "*RGD3402*ReloadAfterSave*", _ => "*RGD3402*SimulateImmediateWrites*"
        });
    }

    [Fact]
    public void UnusedRegistrationsDoNotCreateCommandReceiptsOrRewriteStoreDeclarations()
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("Unused.Contracts").UseGeneratedNamespace("Unused.Persistence")
            .UseScriptedTestDoubles("Unused.Testing").UseInMemoryTestStores().UseInMemoryTestCommands());
        var entity = builder.Entity<MemoryWriteNote>(); entity.ReadEnabled = false;
        entity.HasInMemoryStore(row => row.Id, row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
        entity.HasCommand("Stage").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Title, "portable")).Stage().UseInMemoryTranslations();
        var before = RepositoryGenerator.Generate(context, builder.Build()); InMemoryCommandTranslationTestHarness.Register(builder);
        RepositoryGenerator.Generate(context, builder.Build()).Files.Select(file => file.Contents).Should().Equal(before.Files.Select(file => file.Contents));
        entity.HasInMemoryStore(row => TranslationCalls.Echo(row.Id), row => new MemoryWriteNote { Id = row.Id, Title = row.Title, Version = row.Version, Hidden = row.Hidden });
        var model = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: builder.Build());
        model.Entities.Single().Commands.Single().InMemoryCommand!.Supported.Should().BeFalse();
        model.Entities.Single().InMemoryStore!.KeySelectorSource.Should().Contain("TranslationCalls.Echo");
    }

    [Fact]
    public void ACommandWithNoReadBackendStillValidatesReplacementCyclesAndCapturesWithoutExecutingThem()
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = InMemoryCommandTranslationTestHarness.Builder(translations: false, queries: false);
        builder.HasInMemoryTranslation<int, int>("Cycle", value => TranslationCalls.Echo(value), value => TranslationCalls.Echo(value));
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD4003*");
    }

    [Fact]
    public void ScalarTranslationsCannotTurnNavigationDependentWriteSelectionIntoDetachedReadLoading()
    {
        using var context = MemoryCollectionTestHarness.Context();
        var builder = SavedResultTestHarness.Builder(); InMemoryCommandTranslationTestHarness.Register(builder);
        builder.Entity<MemoryCollectionParent>().HasCommand("UnsafeGraph")
            .Where(row => row.Children.Any(child => TranslationCalls.Positive(child.Version)))
            .Patch(setters => setters.SetProperty(row => row.Name, "changed")).Save().UseInMemoryTranslations();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("*RGD3402*Navigation-dependent write selection*");
    }

    [Fact]
    public void CreateReceiptsDoNotClaimUnusedRootVisibilityUnlessTheResultReadsSavedRows()
    {
        using var context = MemoryWriteTestHarness.Context();
        var builder = InMemoryCommandTranslationTestHarness.Builder(queries: false);
        foreach (var saved in new[] { false, true })
        {
            var command = builder.Entity<MemoryWriteNote>().HasCommand(saved ? "SavedCreateReceipt" : "CreateReceipt")
                .Create<MemoryWriteRequest>(request => new MemoryWriteNote { Id = request.Id, Title = TranslatedWriteCalls.Normalize(request.Title), Version = 1, Hidden = false })
                .AllowWrite(row => row.Id).AllowWrite(row => row.Version).Save().UseInMemoryTranslations();
            if (saved) command.ReturnsSaved(row => row.Id); else command.Returns(row => row.Id);
        }
        var model = RepositoryModelFactory.Create(context, typeof(MemoryWriteContext).Assembly, design: builder.Build());
        var create = model.Entities.Single().Commands.Single(command => command.MethodName == "CreateReceiptAsync");
        create.InMemoryCommand!.Translations!.Select(translation => translation.Name).Should().NotContain("Length");
        model.Entities.Single().Commands.Single(command => command.MethodName == "SavedCreateReceiptAsync").InMemoryCommand!.Translations!.Select(translation => translation.Name).Should().Contain("Length");
    }
}
