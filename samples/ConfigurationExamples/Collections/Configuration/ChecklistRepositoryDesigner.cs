// Paired with ../Model/CollectionsContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/CollectionsRepositories.g.cs; implementation: ../Generated/Repositories.g.cs.
// See ../README.md for the declaration-to-output map.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Collections;
using RonSijm.RepoGen.ConfigurationExamples.Models.Collections;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class ChecklistRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        // No cache/resolver/lease/provider extension is needed for this optional tracked command.
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.Collections")
            .GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<Checklist>(entity =>
        {
            entity.ReadEnabled = false;
            entity.HasCommand("SaveItems")
                .Where<SaveChecklistItemsRequest>((list, request) => list.Id == request.ChecklistId && list.OwnerId == request.OwnerId)
                .Patch(setters => setters.SetProperty(list => list.Version, list => list.Version + 1))
                .AllowWrite(list => list.Version)
                .ExpectVersion<int, SaveChecklistItemsRequest>(list => list.Version, (list, request) => request.ExpectedVersion)
                .ReconcileCollection<ChecklistItem, ChecklistItemInput, string, SaveChecklistItemsRequest>(
                    list => list.Items, request => request.Items, item => item.Key, input => input.Key,
                    children => children.MaximumItems(100)
                        .Add(input => new ChecklistItem { Key = input.Key, Text = input.Text })
                        .Update(setters => setters.SetProperty(item => item.Text, (item, input) => input.Text))
                        .MissingItems(RepositoryCollectionRemovalBehavior.Delete))
                .Save().Returns(list => new ChecklistView(list.Id, list.Version,
                    list.Items.OrderBy(item => item.Key).Select(item => new ChecklistItemInput(item.Key, item.Text)).ToArray()));

            // A separate named operation makes the safe merge policy explicit: no Add and default Keep.
            entity.HasCommand("MergeItems")
                .Where<SaveChecklistItemsRequest>((list, request) => list.Id == request.ChecklistId && list.OwnerId == request.OwnerId)
                .Patch(setters => setters.SetProperty(list => list.Version, list => list.Version + 1))
                .AllowWrite(list => list.Version)
                .ExpectVersion<int, SaveChecklistItemsRequest>(list => list.Version, (list, request) => request.ExpectedVersion)
                .ReconcileCollection<ChecklistItem, ChecklistItemInput, string, SaveChecklistItemsRequest>(
                    list => list.Items, request => request.Items, item => item.Key, input => input.Key,
                    children => children.MaximumItems(100)
                        .Update(setters => setters.SetProperty(item => item.Text, (item, input) => input.Text)))
                .Save();
        });
    }
}
