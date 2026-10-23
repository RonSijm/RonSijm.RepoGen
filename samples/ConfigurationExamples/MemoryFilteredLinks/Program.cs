using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryFilteredLinks;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryFilteredLinks;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryFilteredLinks;

await using var provider = new ServiceCollection().AddFilteredCatalogContextRepositoryTestDoubles().BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<FilteredCatalogContextRepositoryFixture>();
var articles = scope.ServiceProvider.GetRequiredService<IReadFilteredArticleRepository>();
var writer = scope.ServiceProvider.GetRequiredService<IWriteFilteredArticleRepository>();
var labels = scope.ServiceProvider.GetRequiredService<IReadFilteredLabelRepository>();
fixture.Data.FilteredArticleStore.SeedRange([new(1, 1, "North"), new(2, 1, "South"), new(1, 2, "Empty"), new(1, 3, "Archived", true)]);
fixture.Data.FilteredLabelStore.SeedRange([new(1, "Shared"), new(2, "Archived", true), new(3, "Private", Hidden: true), new(4, "Hidden link")]);
fixture.Data.FilteredArticleLabelStore.SeedRange([new(1, 1, 1, 1), new(2, 1, 1, 2), new(3, 1, 1, 3), new(4, 1, 1, 4, true),
    new(5, 2, 1, 2), new(6, 1, 3, 1)]);
fixture.Data.CaptureBaseline();
var cards = await articles.CardsAsync();
if (cards.Count != 3 || !cards[0].Labels.SequenceEqual(["Shared", "Private"]) || cards[2].Labels.Count != 0)
    throw new InvalidOperationException("Filtered required targets omit links, not their article; join filters and root filters remain separate.");
var usage = await labels.UsageAsync();
if (usage.Count != 2 || !usage[0].Articles.SequenceEqual(["North"]))
    throw new InvalidOperationException("The inverse excludes archived articles/links, while label visibility is only a label-root policy.");
var page = await articles.PageAsync(3, 1);
if (page.TotalCount != 3 || page.Items[0].Title != "South") throw new InvalidOperationException("Paging counts surviving roots, not links.");
var all = await articles.AllCardsAsync();
if (all.Count != 4 || all[0].Labels.Count != 4) throw new InvalidOperationException("IgnoreQueryFilters bypasses model filters on every participating table.");
var saved = await writer.RenameAsync(new RenameArticleInput(1, 1, "Renamed"));
if (saved.Status != CommandStatus.Applied || saved.Value is not { Title: "Renamed" } || saved.Value.Labels.Count != 2)
    throw new InvalidOperationException("A scalar save returns a fresh filtered many-to-many DTO, not a persisted graph.");
cards[0].Labels.Clear();
if ((await articles.CardsAsync())[0].Labels.Count != 2 || fixture.Data.FilteredArticleLabelStore.Count != 6)
    throw new InvalidOperationException("Detached projections and filtered reads do not remove saved links.");
fixture.Data.RestoreBaseline();
if ((await articles.CardsAsync())[0].Title != "North") throw new InvalidOperationException("Baseline restore retains all three tables.");
Console.WriteLine("MemoryFilteredLinks: model-filtered endpoints/joins, inverse reads, pages and fresh saved DTOs passed without EF.");
