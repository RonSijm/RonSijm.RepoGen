using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryScopedOptional;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryScopedOptional;

await using var provider = new ServiceCollection().AddScopedReviewContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<ScopedReviewContextRepositoryFixture>();
var reader = scope.ServiceProvider.GetRequiredService<IScopedReviewReader>();
var writer = scope.ServiceProvider.GetRequiredService<IScopedReviewWriter>();
fixture.Data.ReviewFolderStore.SeedRange([new(1, 1), new(2, 1)]);
fixture.Data.ReviewResourceStore.SeedRange([new(1), new(2), new(3), new(4)]);
fixture.Data.ReviewPersonStore.SeedRange([new(1, 1, "eu", "a", "Alice"), new(2, 1, "us", "b", "Bob"), new(3, 2, "eu", "a", "Other partition")]);
fixture.Data.ReviewLinkStore.SeedRange([new(1, 1, 1, "eu", "a", "present"), new(1, 1, 2, null, null, "absent"), new(2, 1, 1, "eu", "a", "other")]);
fixture.Data.CaptureBaseline();

foreach (var invalid in new[] { new ReviewReferenceInput(1, 3, "eu", null, "partial"), new ReviewReferenceInput(2, 3, null, null, "absent but cross-owner") })
{
    try
    {
        await writer.ReplaceAsync(new(1, 1, [invalid]));
        throw new InvalidOperationException("New optional references must be complete and owner-consistent, including absence.");
    }
    catch (ArgumentException) { }
}
if (fixture.Data.PendingCount != 0 || (await reader.FindAsync(new(1, 1)))!.Version != 1)
    throw new InvalidOperationException("Invalid reference input must not leave parent or link changes.");

var saved = await writer.ReplaceAsync(new(1, 1, [new(999, 1, null, "partial ignored", "keep present"), new(999, 2, "us", "b", "keep absent"),
    new(1, 3, null, null, "new absent"), new(1, 4, "us", "b", "new present")]));
if (saved.Status != CommandStatus.Applied || saved.Value!.Links.Count != 4 || saved.Value.Links[0].ReviewerName != "Alice"
    || saved.Value.Links[1].ReviewerName is not null || saved.Value.Links[2].ReviewerName is not null || saved.Value.Links[3].ReviewerName != "Bob")
    throw new InvalidOperationException("New references may be absent/present; matched reference inputs must remain ignored.");
if (fixture.Data.ReviewLinkStore.Find((1, 1, 3))!.PartitionId != 1)
    throw new InvalidOperationException("Absent references must not null or transfer shared ownership.");

var snapshot = saved.Value;
var stage = await writer.StageAsync(new(1, 1, [new(999, 1, null, "ignored", "staged present", snapshot.Links[0].Version),
    new(999, 3, "ignored", null, "staged absent", snapshot.Links[2].Version)], snapshot.Version));
if (stage.Status != CommandStatus.Staged || (await reader.FindAsync(new(1, 1)))!.Links.Count != 4)
    throw new InvalidOperationException("Stage must leave saved reads unchanged.");
if (await fixture.Data.SaveChangesAsync() != 5 || (await reader.FindAsync(new(1, 1)))!.Links.Count != 2)
    throw new InvalidOperationException("Shared Save must apply only approved parent/link changes.");
if (fixture.Data.ReviewLinkStore.Find((2, 1, 1))!.Caption != "other" || fixture.Data.ReviewPersonStore.Count != 3)
    throw new InvalidOperationException("Other owners and referenced people must remain unchanged.");
await using (await fixture.Data.BeginTransactionAsync())
    await writer.ReplaceAsync(new(1, 1, [], 3));
if ((await reader.FindAsync(new(1, 1)))!.Links.Count != 2) throw new InvalidOperationException("Rollback must restore optional reference state.");
fixture.Data.RestoreBaseline();
if ((await reader.FindAsync(new(1, 1)))!.Version != 1 || fixture.Data.ReviewLinkStore.Count != 3)
    throw new InvalidOperationException("Baseline restore must restore the original graph.");
Console.WriteLine("MemoryScopedOptional: explicit owner-shared optional references, independent null policy, immutable matches, Stage/Save and rollback passed without EF.");
