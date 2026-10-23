using RonSijm.RepoGen.AdventureWorks.Model;
using RonSijm.RepoGen.Tool;

namespace RonSijm.RepoGen.AdventureWorks.Generation;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--check")
        {
            Console.Error.WriteLine("Usage: AdventureWorks.Generation <example-directory> [--check]");
            return 2;
        }

        using var context = AdventureWorksDatabase.CreateContext();
        var bundle = RepositoryGenerator.Generate(context, new AdventureWorksRepositoryDesigner(), RepositoryOutputLayout.PerEntity);
        if (bundle.EntityCount != 71)
        {
            throw new InvalidOperationException($"Expected repositories for all 71 tables, found {bundle.EntityCount}.");
        }

        var root = Path.GetFullPath(args[0]);
        var output = new RepositoryOutputLocations(
            Path.Combine(root, "Demo/Generated"), Projections: Path.Combine(root, "Demo/Generated"),
            Manifest: Path.Combine(root, "Demo/Generated/manifest.json"));
        if (args.Length == 2)
        {
            var differences = bundle.Check(output);
            foreach (var difference in differences)
            {
                Console.Error.WriteLine($"{difference.DiagnosticId}: {difference.Path}: {difference.Message}");
            }

            Console.WriteLine($"AdventureWorks: checked {bundle.EntityCount} repositories; {differences.Count} differences.");
            return differences.Count == 0 ? 0 : 3;
        }

        bundle.Write(output);
        Console.WriteLine($"AdventureWorks: generated {bundle.EntityCount} repositories in {bundle.Files.Count} files without a database connection.");
        return 0;
    }
}
