// Paired with ../Model/MembershipDirectoryContext.cs by ../../GenerationRunner/Program.cs.
// API: ../../Contracts/Generated/MembershipRepositories.g.cs; implementation: ../Generated/Repositories.g.cs.
// See ../README.md for the declaration-to-output map; the consumer does not run this designer.
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.Membership;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class MembershipDirectoryRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.Membership"));
        // DTO fragments do not register DTOs as fictitious EF entities.
        var minimumCount = modelBuilder.HasFragment<DirectoryCategoryCount, DirectoryCountSearch>("MinimumCount",
            (total, criteria) => total.Count >= criteria.MinimumCount);
        modelBuilder.Entity<DirectoryItem>(entity =>
        {
            entity.WriteEnabled = false;
            entity.ProjectionEnabled = false;
            entity.BindReadPort<IMembershipDirectory>();
            entity.HasVisibilityPolicy("Active", item => item.Active);
            entity.HasProjection(item => new DirectoryItemSummary(item.Id, item.Name, item.Category));
            var minimumId = entity.HasFragment<DirectorySearch>("MinimumId", (item, criteria) => item.Id >= criteria.MinimumId);
            entity.HasQuery("FindByIds")
                .WhereIn(item => item.Id, (int[] ids) => ids, options => options.Distinct().MaximumCount(32))
                .OrderBy(item => item.Id).ProjectTo<DirectoryItemSummary>().List();
            entity.HasQuery("Search")
                .Validate<DirectorySearch>(request => request.MinimumId >= 0, "MinimumId must be non-negative.")
                .Use(minimumId.WithRequestName("request"))
                .WhereIn(item => item.Category, (DirectorySearch request) => request.Categories,
                    options => options.NullInput(RepositoryMembershipInputBehavior.IgnoreFilter)
                        .NullValues(RepositoryMembershipNullValueBehavior.Ignore).Distinct().MaximumCount(16))
                .OrderBy(item => item.Id).ProjectTo<DirectoryItemSummary>().List();
            entity.HasQuery("CategoryCounts")
                .GroupBy(item => item.Category, group => new DirectoryCategoryCount { Category = group.Key, Count = group.Count() })
                .Results<DirectoryCategoryCount>()
                .WhereIn(total => total.Category, (string[] categories) => categories, options => options.Distinct().MaximumCount(16))
                .OrderBy(total => total.Category).List();
            // Collision policy and stable row order are design-time choices, not service-side grouping.
            entity.HasQuery("FirstByCategory").Results<DirectoryItem>().OrderBy(item => item.Id)
                .Dictionary(item => item.Category, item => new DirectoryItemSummary(item.Id, item.Name, item.Category), RepositoryDuplicateKeyBehavior.KeepFirst);
            entity.HasQuery("LastByCategory").Results<DirectoryItem>().OrderBy(item => item.Id)
                .Dictionary(item => item.Category, item => new DirectoryItemSummary(item.Id, item.Name, item.Category), RepositoryDuplicateKeyBehavior.KeepLast);
            var blankName = entity.HasFragment("BlankName", item => item.Name.Length == 0);
            var textMinimum = entity.HasFragment<DirectoryTextSearch>("TextMinimum", (item, bounds) => item.Id >= bounds.MinimumId);
            var nameText = entity.HasFragment<DirectoryTextSearch>("NameText", (item, text) => item.Name.Contains(text.Term!));
            var categoryText = entity.HasFragment<DirectoryTextSearch>("CategoryText", (item, selection) => item.Category.Contains(selection.Term!));
            entity.HasQuery("SearchText")
                .Validate<DirectoryTextSearch>(request => request.MinimumId >= 0, "MinimumId must be non-negative.")
                .Use(blankName.Not().And(textMinimum).WithRequestName("request"))
                .UseIf<DirectoryTextSearch>(request => request.Term != null && request.Term.Length > 0, nameText.Or(categoryText))
                .OrderBy(item => item.Id).ProjectTo<DirectoryItemSummary>().List();
            entity.HasQuery("CategoryCountsAtLeast")
                .Validate<DirectoryCountSearch>(request => request.MinimumCount >= 0, "MinimumCount must be non-negative.")
                .GroupBy(item => item.Category, group => new DirectoryCategoryCount { Category = group.Key, Count = group.Count() })
                .Results<DirectoryCategoryCount>()
                .UseIf<DirectoryCountSearch>(request => request.MinimumCount > 0, minimumCount)
                .OrderBy(total => total.Category).List();
        });
    }
}
