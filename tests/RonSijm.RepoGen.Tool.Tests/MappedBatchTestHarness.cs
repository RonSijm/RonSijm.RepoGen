using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MappedBatchTestHarness
{
    private static readonly Lazy<Assembly> Production = new(() =>
    {
        using var context = MappedSetterTestHarness.Context();
        return MappedSetterTestHarness.CompileProduction(context, Builder());
    });
    private static readonly Lazy<Assembly> Testing = new(() =>
    {
        using var context = MappedSetterTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, Builder().Build()));
    });
    internal static MappedSetterSeed Third => new(3, "another", "second", 30, 5, null, "three");

    internal static RepositoryModelBuilder Builder(bool state = true, bool backend = true, bool strict = true)
    {
        var builder = MappedSetterTestHarness.Builder(state, backend, strict);
        var note = builder.Entity<MappedSetterNote>();
        foreach (var name in new[] { "Apply", "Stage", "Execute", "Conditional", "CurrentValues", "Break" }) note.ExcludeCommand(name);
        foreach (var name in new[] { "SaveBatch", "StageBatch", "CurrentBatch", "ConditionalBatch", "ConstantBatch", "FailValueBatch", "FailBatch" })
        {
            var command = note.HasCommand(name);
            command.PatchMany<MappedSetterRequest, int, IReadOnlyList<MappedSetterRequest>>(items => items, row => row.Id, item => item.Id, setters =>
            {
                if (name == "ConditionalBatch")
                {
                    setters.SetPropertyWhen<string>(nameof(MappedSetterNote.Caption), (row, item) => item.Caption, item => item.ChangeCaption);
                    return;
                }
                if (name == "ConstantBatch")
                {
                    setters.SetProperty<int>("audit", 40)
                        .SetProperty(nameof(MappedSetterNote.Caption), row => row.Caption + EF.Property<int>(row, "audit"))
                        .SetProperty<string?>(MappedSetterContext.Extra, (string?)null)
                        .SetProperty(row => row.Title, "mixed");
                    return;
                }
                if (name == "CurrentBatch")
                    setters.SetPropertyWhen<string>(nameof(MappedSetterNote.Caption), (row, item) => row.Caption + "!", item => item.ChangeCaption);
                else if (name == "FailBatch")
                    setters.SetPropertyWhen<string>(nameof(MappedSetterNote.Caption), (row, item) => item.Caption == "fail" ? null! : item.Caption, item => item.ChangeCaption);
                else setters.SetPropertyWhen<string>(nameof(MappedSetterNote.Caption), (row, item) => item.Caption, item => item.ChangeCaption);
                if (name == "FailValueBatch") setters.SetProperty<int>("audit", (row, item) => 100 / (item.Version - 5));
                else setters.SetProperty<int>("audit", row => EF.Property<int>(row, "audit") + 1);
                if (name == "CurrentBatch") setters.SetProperty<string?>(MappedSetterContext.Extra, row => (string?)row[MappedSetterContext.Extra] + "!");
                else setters.SetProperty<string?>(MappedSetterContext.Extra, (row, item) => item.Extra);
                setters.SetProperty<int>(MappedSetterContext.Version, (row, item) => item.Version + 1)
                    .ExpectVersion<int>(MappedSetterContext.Version, item => item.Version);
            }, maximumItems: 3).AllowWrite<int>(MappedSetterContext.Version);
            if (name == "StageBatch") command.Stage();
            else command.Save().ReturnsSaved(MappedSetterTestHarness.Projection);
            if (name == "FailBatch") command.RequireTestOverride("Real-provider NOT NULL constraint failure is not simulated.");
        }
        return builder;
    }

    internal static async Task SeedAsync(MappedSetterContext context)
    {
        await MappedSetterTestHarness.SeedAsync(context);
        var row = new MappedSetterNote { Id = Third.Id, Title = Third.Title, UniqueCode = Third.UniqueCode };
        context.Add(row);
        context.Entry(row).Property(nameof(MappedSetterNote.Caption)).CurrentValue = Third.Caption;
        context.Entry(row).Property("audit").CurrentValue = Third.Audit;
        context.Entry(row).Property(MappedSetterContext.Version).CurrentValue = Third.Version;
        context.Entry(row).Property(MappedSetterContext.Extra).CurrentValue = Third.Extra;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    internal static object Operations(MappedSetterContext context) => Activator.CreateInstance(Production.Value.GetType(
        MappedSetterTestHarness.Namespace + ".Persistence.MappedSetterNoteRepositoryOperations")!, context)!;

    internal static (object Repository, InMemoryWriteDataContext Data, InMemoryStore<MappedSetterSeed, int> Store) Fixture(Assembly? assembly = null)
    {
        var fixture = MappedSetterTestHarness.Fixture(assembly ?? Testing.Value);
        fixture.Store.Seed(Third);
        return fixture;
    }

    internal static Task<T> Call<T>(object repository, string method, params MappedSetterRequest[] items)
        => MappedStateTestHarness.Call<T>(repository, method + "Async", (IReadOnlyList<MappedSetterRequest>)items);
}
