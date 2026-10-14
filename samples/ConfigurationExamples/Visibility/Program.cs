// Repository usage and executable assertions, not generator configuration.
// Start with Configuration/VisibilityRepositoryDesigner.cs and README.md.
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.Visibility;
using RonSijm.RepoGen.ConfigurationExamples.Models.Visibility;

namespace RonSijm.RepoGen.ConfigurationExamples.Visibility;

internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddDbContext<VisibilityContext>(options => options.UseSqlite("Data Source=:memory:"));
        services.AddVisibilityContextRepositories();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<VisibilityContext>();
        await context.Database.OpenConnectionAsync();
        await context.Database.EnsureCreatedAsync();
        context.Owners.AddRange(new ShowcaseOwner { Id = 1, Name = "Ada", Active = true }, new ShowcaseOwner { Id = 2, Name = "Grace", Active = false });
        context.Documents.AddRange(new ShowcaseDocument
        {
            Id = 1,
            Title = "Current",
            Active = true,
            OwnerId = 1,
            Comments = [new ShowcaseComment { Id = 11, Text = "Public note", Public = true }, new ShowcaseComment { Id = 12, Text = "Private note", Public = false }]
        },
            new ShowcaseDocument { Id = 2, Title = "Historical owner", Active = true, OwnerId = 2 }, new ShowcaseDocument { Id = 3, Title = "No owner", Active = true },
            new ShowcaseDocument { Id = 4, Title = "Hidden document", Active = false, OwnerId = 1 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Application service sees only this EF-free, handwritten port. It has no runtime policy/load bypass methods.
        var library = scope.ServiceProvider.GetRequiredService<IReviewedLibrary>();
        var summaries = await library.GetSummariesAsync();
        Check(summaries.Select(document => document.Id).SequenceEqual([1, 2, 3]), "root visibility");
        Check(summaries[0].OwnerName == "Ada" && summaries[1].OwnerName is null, "global-filtered optional navigation");
        var detail = await library.GetDetailsAsync(1);
        Check(detail is not null && detail.Comments.SequenceEqual(["Public note"]), "explicit filtered graph loading");
        Check(await library.GetDetailsAsync(4) is null, "hidden root detail");
        var history = await library.GetTrustedHistoryAsync();
        Check(history[1].OwnerName == "Grace" && history.Count == 3, "reviewed historical navigation, retained root visibility");
        Check((await library.GetWithoutOwnersAsync()).SequenceEqual([2, 3]), "policy-aware anti-join");
        Check(!context.ChangeTracker.Entries().Any(), "read-only detached results");
        Console.WriteLine("Visibility: reviewed projections, filtered loading, global policies, explicit historical read and policy-aware anti-join passed.");

        static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
