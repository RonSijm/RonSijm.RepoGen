namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.ProxyShelves;

public sealed record ShelfCard(int Id, string Name, List<ShelfItemCard> Items);
