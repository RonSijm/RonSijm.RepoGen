using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAtomicResults;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryAtomicResults;

namespace RonSijm.RepoGen.ConfigurationExamples.MemoryAtomicResults;

internal static class Program
{
    private static async Task Main()
    {
        await using var provider = new ServiceCollection().AddEnrollmentContextRepositoryTestDoubles().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var fixture = scope.ServiceProvider.GetRequiredService<EnrollmentContextRepositoryFixture>();
        var writer = scope.ServiceProvider.GetRequiredService<IEnrollmentWriter>();
        fixture.Data.CourseStore.Seed(new(1, "Design"));
        fixture.Data.EnrollmentStore.Seed(new(1, 1, "a", "Alice", 1));
        fixture.Data.CaptureBaseline();
        var inserted = await writer.EnsureAsync(new(2, 1, "b", "Bob"));
        if (inserted.Status != CommandStatus.Inserted || inserted.Value is not { Id: 2, Course: "Design" } ||
            !inserted.Value.Members.SequenceEqual(["Alice", "Bob"]))
        {
            throw new InvalidOperationException("Fresh results must see saved related rows and the prepared new root.");
        }
        var updated = await writer.PutAsync(new(900, 1, "a", "Alicia"));
        if (updated.Status != CommandStatus.Updated || updated.Value is not { Id: 1, Version: 2 } ||
            !updated.Value.Members.SequenceEqual(["Alicia", "Bob"]) || fixture.Data.EnrollmentStore.Find(900) is not null)
        {
            throw new InvalidOperationException("Fresh upsert results must use the saved primary key, not the candidate identity or old version.");
        }
        var stale = await writer.PutAsync(new(900, 1, "a", "stale"));
        if (stale.Status != CommandStatus.NotMatched || stale.HasValue)
        {
            throw new InvalidOperationException("An unmatched original version must not return existing data.");
        }
        if ((await writer.EnsureAsync(new(901, 1, "a", "duplicate"))).Status != CommandStatus.AlreadyExists)
        {
            throw new InvalidOperationException("Conflict detection must remain independent of result loading.");
        }
        fixture.Data.RestoreBaseline();
        if (fixture.Data.EnrollmentStore.Count != 1 || fixture.Data.EnrollmentStore.Find(1)!.Name != "Alice")
        {
            throw new InvalidOperationException("Baseline restore must include all atomic branches.");
        }
        Console.WriteLine("MemoryAtomicResults: fresh atomic DTO graphs, saved identity, versions and baseline restore passed without EF/models.");
    }
}
