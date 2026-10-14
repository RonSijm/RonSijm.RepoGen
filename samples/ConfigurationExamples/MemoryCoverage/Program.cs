using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryCoverage;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryCoverage;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryCoverage;
using RonSijm.RepoGen.Testing;

using var provider = new ServiceCollection().AddMemoryCoverageContextRepositoryTestDoubles().BuildServiceProvider();
using var scope = provider.CreateScope();
var repository = scope.ServiceProvider.GetRequiredService<IReviewedEntryRepository>();
var doubleRepository = scope.ServiceProvider.GetRequiredService<ReviewedEntryRepositoryTestDouble>();
doubleRepository.Fixture.Data.ReviewedEntryStore.SeedRange(new ReviewedEntry[]
{
    new() { Id = 1, Name = "visible" }, new() { Id = 2, Name = "hidden", Hidden = true }
});
Ensure((await repository.PortableListAsync()).Single().Id == 1, "RepoGen visibility remains applied.");
try { await repository.EfFilteredCountAsync(); throw new InvalidOperationException("An EF filter was silently ignored."); }
catch (UnsupportedInMemoryRepositoryOperationException failure) { Ensure(failure.Reason.Contains("Active EF model query filters", StringComparison.Ordinal), "Coverage reason."); }
try { await repository.WithReviewerAsync(); throw new InvalidOperationException("Navigation was silently emulated."); }
catch (UnsupportedInMemoryRepositoryOperationException failure) { Ensure(failure.Reason.Contains("Navigation traversal", StringComparison.Ordinal), "Navigation receipt."); }

doubleRepository.OnEfFilteredCountAsync.Returns(1);
Ensure(await repository.EfFilteredCountAsync() == 1, "Explicit provider-operation arrangement.");
doubleRepository.OnWithReviewerAsync.Returns(new[] { new ReviewedItemWithReviewer(1, "visible", "arranged reviewer") });
Ensure((await repository.WithReviewerAsync()).Single().Reviewer == "arranged reviewer", "Typed DTO override.");
doubleRepository.OnDeleteAsync.Returns(1);
Ensure(await repository.DeleteAsync(1) == 1 && doubleRepository.Fixture.Data.ReviewedEntryStore.Count == 2,
    "A scripted write result does not imply simulated persistence.");
doubleRepository.ResetTestDouble();
Ensure((await repository.PortableListAsync()).Count == 1, "Reset retains portable execution.");
try { await repository.DeleteAsync(1); throw new InvalidOperationException("Unscripted write accepted."); }
catch (UnsupportedInMemoryRepositoryOperationException) { }
Console.WriteLine("MemoryCoverage: strict designer acknowledgements, explicit coverage failures and typed overrides passed without EF.");

static void Ensure(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
