using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryPartitions;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryPartitions;
using RonSijm.RepoGen.Testing;

// Sharing across request scopes is explicit. Default fixture registration is scoped, not singleton.
var shared = new PartitionDirectoryContextRepositoryFixture();
var services = new ServiceCollection().AddSingleton(shared).AddPartitionDirectoryContextRepositoryTestDoubles();
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
using var firstScope = provider.CreateScope();
using var secondScope = provider.CreateScope();
var first = firstScope.ServiceProvider.GetRequiredService<IPartitionSettingRepositoryTestDouble>();
var second = secondScope.ServiceProvider.GetRequiredService<IPartitionSettingRepositoryTestDouble>();
Ensure(!ReferenceEquals(first, second) && ReferenceEquals(first.Fixture, second.Fixture), "Each scope has its own scripts, with explicitly shared data.");

var eastKey = new PartitionKey("east", "primary");
var westKey = new PartitionKey("west", "primary");
var east = shared.For(eastKey);
var west = shared.For(westKey);
east.PartitionSettingStore.Seed(new() { Code = "color", Value = "blue" });
west.PartitionSettingStore.Seed(new() { Code = "color", Value = "green" });
east.CaptureBaseline();
Arrange(first);
Arrange(second);
var reader = secondScope.ServiceProvider.GetRequiredService<ISettingLookup>();
Ensure((await reader.FindAsync(new(eastKey, "color")))?.Value == "blue", "An explicitly arranged handler selects the request's typed partition.");
Ensure((await reader.FindAsync(new(westKey, "color")))?.Value == "green", "Equal row keys cannot leak across context partitions.");
Ensure(ReferenceEquals(east, shared.For(new("east", "primary"))), "Equal context keys retain stable data/store identities.");

east.Clear();
east.RestoreBaseline();
Ensure(east.PartitionSettingStore.Find("color")?.Value == "blue" && west.PartitionSettingStore.Find("color")?.Value == "green",
    "Baseline reset is local to one context key.");
first.ResetTestDouble();
Ensure((await reader.FindAsync(new(eastKey, "color")))?.Value == "blue", "Resetting one scope's scripts leaves shared data and other scripts intact.");
try { await first.FindAsync(new(eastKey, "color")); throw new InvalidOperationException("Reset must remove first-scope scripts."); }
catch (UnarrangedRepositoryOperationException) { }
Ensure(shared.ContextKeys.Count == 2, "The fixture exposes a detached inventory of created partitions.");
Console.WriteLine("MemoryPartitions: typed partitions, public storage proxies and explicit cross-scope data sharing passed without EF/models.");

static void Arrange(IPartitionSettingRepositoryTestDouble repository) => repository.OnFindAsync.Runs((args, _) =>
{
    var seed = repository.Fixture.For(args.Request.Partition).PartitionSettingStore.Find(args.Request.Code);
    return Task.FromResult(seed is null ? null : new SettingView(seed.Code, seed.Value));
});

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
