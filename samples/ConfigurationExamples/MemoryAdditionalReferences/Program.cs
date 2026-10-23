using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryAdditionalReferences;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryAdditionalReferences;

await using var provider = new ServiceCollection().AddReferenceCatalogContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<ReferenceCatalogContextRepositoryFixture>();
var reads = scope.ServiceProvider.GetRequiredService<ICollectionMembershipReader>();
var writes = scope.ServiceProvider.GetRequiredService<ICollectionMembershipWriter>();
fixture.Data.StoredCollectionStore.SeedRange([new(1, 1), new(2, 1)]);
fixture.Data.StoredCatalogItemStore.SeedRange([new(1, "First"), new(2, "Second"), new(3, "Third")]);
fixture.Data.StoredContributorStore.SeedRange([new(1, "eu", "a", "Contributor A"), new(2, "us", "b", "Contributor B")]);
fixture.Data.StoredImportSourceStore.SeedRange([new(1, "Source A"), new(2, "Source B")]);
fixture.Data.StoredMembershipStore.SeedRange([new(1, 1, "eu", "a", 1, "first", 1), new(1, 2, "us", "b", 2, "second", 1), new(2, 1, "us", "b", 2, "other", 1)]);
fixture.Data.CaptureBaseline();
var result = await writes.ReplaceAsync(new(1, 1, [new(1, "us", "b", 2, "changed"), new(3, "us", "b", 2, "new")]));
if (result.Status != CommandStatus.Applied || result.Value!.Items[0].Contributor != "Contributor A" || result.Value.Items[1].Source != "Source B")
    throw new InvalidOperationException("Matched references must stay immutable; new links use their complete explicit references.");
if (fixture.Data.StoredMembershipStore.Find((1, 1))!.SourceId != 1 || fixture.Data.StoredMembershipStore.Find((2, 1))!.Caption != "other")
    throw new InvalidOperationException("Neither matched reference values nor another owner can be transferred.");
var staged = await writes.StageAsync(new(1, 2, [new(2, "eu", "a", 1, "staged")]));
if (staged.Status != CommandStatus.Staged || (await reads.FindAsync(1))!.Items.Count != 2)
    throw new InvalidOperationException("Stage must not change saved reads.");
await fixture.Data.SaveChangesAsync();
if ((await reads.FindAsync(1))!.Items.Single().Source != "Source A" || fixture.Data.StoredContributorStore.Count != 2 || fixture.Data.StoredImportSourceStore.Count != 2)
    throw new InvalidOperationException("Only join rows are saved; referenced entities are not created or mutated.");
await using (await fixture.Data.BeginTransactionAsync())
    await writes.ReplaceAsync(new(1, 3, [new(3, "us", "b", 2, "rollback")]));
if ((await reads.FindAsync(1))!.Items.Single().ItemId != 2) throw new InvalidOperationException("Rollback must restore references and versions.");
fixture.Data.RestoreBaseline();
if ((await reads.FindAsync(1))!.Version != 1) throw new InvalidOperationException("Baseline restore must restore all fixture stores.");
Console.WriteLine("MemoryAdditionalReferences: explicit target plus composite/scalar create-only references, immutable matched FKs, saved DTOs and Stage/Save/rollback passed without EF/models.");
