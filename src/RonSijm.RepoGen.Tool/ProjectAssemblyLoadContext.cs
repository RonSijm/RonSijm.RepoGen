using System.Reflection;
using System.Runtime.Loader;

namespace RonSijm.RepoGen.Tool;

internal sealed class ProjectAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver[] resolvers;

    public ProjectAssemblyLoadContext(params string[] assemblyPaths)
        : base(isCollectible: true)
    {
        resolvers = assemblyPaths.Select(static path => new AssemblyDependencyResolver(path)).ToArray();
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var sharedAssembly = Default.Assemblies.FirstOrDefault(assembly => string.Equals(
            assembly.GetName().Name,
            assemblyName.Name,
            StringComparison.OrdinalIgnoreCase));
        if (sharedAssembly is not null)
        {
            return sharedAssembly;
        }

        var path = resolvers
            .Select(resolver => resolver.ResolveAssemblyToPath(assemblyName))
            .FirstOrDefault(static candidate => candidate is not null);
        path ??= ResolveFromNuGetCache(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    private static string? ResolveFromNuGetCache(AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName.Name))
        {
            return null;
        }

        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }

        var packageDirectory = Path.Combine(packageRoot, assemblyName.Name.ToLowerInvariant());
        if (!Directory.Exists(packageDirectory))
        {
            return null;
        }

        foreach (var candidate in Directory.EnumerateFiles(
                     packageDirectory,
                     assemblyName.Name + ".dll",
                     SearchOption.AllDirectories))
        {
            var candidateName = AssemblyName.GetAssemblyName(candidate);
            if (string.Equals(candidateName.Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase) &&
                candidateName.Version == assemblyName.Version)
            {
                return candidate;
            }
        }

        return null;
    }
}