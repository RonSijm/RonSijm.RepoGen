using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.ProxyDirectory;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.ProxyDirectory;
using RonSijm.RepoGen.Testing;

namespace RonSijm.RepoGen.ConfigurationExamples.MemoryProxies;

internal static class Program
{
    private static readonly string[] ExpectedNames = ["Ada", "Grace"];
    private static readonly string[] ScriptedTags = ["explicit fixture behavior"];

    private static async Task Main()
    {
        using var provider = new ServiceCollection().AddProxyDirectoryContextRepositoryTestDoubles().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var directory = scope.ServiceProvider.GetRequiredService<IProxyDirectory>();
        var repository = scope.ServiceProvider.GetRequiredService<DirectoryRecordRepositoryTestDouble>();
        var data = repository.Fixture.Data;
        data.DirectoryRecordStore.SeedRange(new DirectorySeed[] { new(2, "Grace", 7), new(1, "Ada", 3), new(3, "Hidden", 100, true) });
        data.CaptureBaseline();
        Ensure(await directory.FindAsync(1) == new DirectoryCard(1, "Ada", 30), "Explicit rename/conversion mapping.");
        Ensure(await directory.FindAsync(3) is null, "Visibility reads the explicitly mapped Disabled member.");
        var page = await directory.PageAsync(1, 1);
        Ensure(page.TotalCount == 2 && page.Items.Single().Name == "Ada", "Default sort and counted page.");
        Ensure(await directory.TotalPointsAsync() == 100, "Aggregate uses mapped scalar points.");

        var stream = directory.StreamAsync();
        data.DirectoryRecordStore.Seed(new(4, "Linus", 2));
        var names = new List<string>();
        await foreach (var row in stream) names.Add(row.Name);
        Ensure(names.SequenceEqual(ExpectedNames.Append("Linus")), "Cold stream reads newly arranged proxy data.");
        data.RestoreBaseline();
        Ensure(await directory.TotalPointsAsync() == 100, "Baseline contains public arrangement data, not read-row helpers.");

        try
        {
            await directory.InternalTagsAsync();
            throw new InvalidOperationException("An unmapped scalar must not silently become an empty/default result.");
        }
        catch (UnsupportedInMemoryRepositoryOperationException failure)
        {
            Ensure(failure.Reason.Contains("InternalTag", StringComparison.Ordinal), "Coverage names the missing field.");
        }
        repository.OnInternalTagsAsync.Returns(ScriptedTags);
        Ensure((await directory.InternalTagsAsync()).SequenceEqual(ScriptedTags), "Typed scripts remain available.");
        repository.ResetTestDouble();
        Ensure(await directory.FindAsync(1) == new DirectoryCard(1, "Ada", 30), "Reset retains backend and fixture.");
        Console.WriteLine("MemoryProxies: internal EF models, explicit scalar mappings, visibility/pages/aggregate/stream and typed overrides passed without EF/model dependencies.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
