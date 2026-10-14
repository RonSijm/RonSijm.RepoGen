using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryManyToMany;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryManyToMany;

var services = new ServiceCollection(); services.AddTaggedArticleContextRepositoryTestDoubles();
await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
var fixture = scope.ServiceProvider.GetRequiredService<TaggedArticleContextRepositoryFixture>();
var articles = scope.ServiceProvider.GetRequiredService<IReadTaggedArticleRepository>();
var tags = scope.ServiceProvider.GetRequiredService<IReadArticleTagRepository>();
fixture.Data.TaggedArticleStore.SeedRange([new(1, 1, "North"), new(2, 1, "South"), new(1, 2, "Empty")]);
fixture.Data.ArticleTagStore.SeedRange([new(1, "Shared", false), new(2, "Internal", true), new(3, "Unused", false)]);
fixture.Data.TaggedArticleLinkStore.SeedRange([new(1, 1, 1), new(1, 1, 2), new(2, 1, 1)]);
fixture.Data.CaptureBaseline();

var cards = await articles.CardsAsync();
if (cards.Count != 3 || !cards[0].Tags.SequenceEqual(["Shared", "Internal"]) || !cards[2].Tags.SequenceEqual(["Shared"]) || cards[1].Tags.Count != 0)
    throw new InvalidOperationException("Composite article keys must load the right join rows, shared tags and empty collections.");
if ((await articles.PublicCardsAsync())[0].Tags.Count != 1 || !(await articles.WithTagAsync("Shared")).SequenceEqual(["North", "South"]))
    throw new InvalidOperationException("Child visibility and membership come from the shared designer query, not implicit policy injection.");
var usage = await tags.UsageAsync();
if (usage.Count != 2 || !usage[0].Articles.SequenceEqual(["North", "South"]) || usage[1].Articles.Count != 0)
    throw new InvalidOperationException("Only accessed inverse edges load, while the tag-root visibility policy stays on its root.");
cards[0].Tags.Clear();
if ((await articles.CardsAsync())[0].Tags.Count != 2 || fixture.Data.TaggedArticleLinkStore.Count != 3)
    throw new InvalidOperationException("Returned DTO lists must not mutate stored relationships.");
fixture.Data.TaggedArticleLinkStore.Remove((1, 1, 2));
if ((await articles.CardsAsync())[0].Tags.Count != 1) throw new InvalidOperationException("Every read must capture fresh join-table rows.");
fixture.Data.RestoreBaseline();
if ((await articles.CardsAsync())[0].Tags.Count != 2) throw new InvalidOperationException("Baseline restore must include the explicit join store.");
Console.WriteLine("MemoryManyToMany: composite keys, shared tags, explicit inverses/visibility and detached DTOs passed without EF.");
