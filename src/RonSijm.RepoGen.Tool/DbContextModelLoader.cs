using System.Reflection;
using RonSijm.RepoGen.Design;
using Microsoft.EntityFrameworkCore;
using RepositoryDesignModel = RonSijm.RepoGen.Design.RepositoryDesign;

namespace RonSijm.RepoGen.Tool;

internal static class DbContextModelLoader
{
    public static RepositoryGenerationModel Load(
        string assemblyPath,
        string? designerAssemblyPath,
        string? requestedDesignerName,
        string? requestedContextName,
        bool generateSelectors)
    {
        var loadContext = designerAssemblyPath is null
            ? new ProjectAssemblyLoadContext(assemblyPath)
            : new ProjectAssemblyLoadContext(assemblyPath, designerAssemblyPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var design = designerAssemblyPath is null
                ? null
                : LoadDesigner(loadContext.LoadFromAssemblyPath(designerAssemblyPath), requestedDesignerName);
            var contextType = FindContextType(assembly, requestedContextName);
            using var context = CreateContext(contextType);
            return RepositoryModelFactory.Create(context, assembly, generateSelectors, design);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static RepositoryDesignModel LoadDesigner(
        Assembly assembly,
        string? requestedDesignerName)
    {
        var designerTypes = GetLoadableTypes(assembly)
            .Where(static type =>
                !type.IsAbstract &&
                !type.ContainsGenericParameters &&
                typeof(RepositoryDesigner).IsAssignableFrom(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Type designerType;
        if (!string.IsNullOrWhiteSpace(requestedDesignerName))
        {
            designerType = designerTypes.FirstOrDefault(type =>
                               string.Equals(type.FullName, requestedDesignerName, StringComparison.Ordinal) ||
                               string.Equals(type.Name, requestedDesignerName, StringComparison.Ordinal))
                           ?? throw new InvalidOperationException(
                               $"RepositoryDesigner '{requestedDesignerName}' was not found. Available designers: {FormatContextNames(designerTypes)}.");
        }
        else
        {
            designerType = designerTypes.Length switch
            {
                0 => throw new InvalidOperationException(
                    "The designer project does not contain a concrete RepositoryDesigner."),
                1 => designerTypes[0],
                _ => throw new InvalidOperationException(
                    $"The designer project contains multiple RepositoryDesigners. Use --designer to select one: {FormatContextNames(designerTypes)}."),
            };
        }

        var designer = (RepositoryDesigner?)Activator.CreateInstance(designerType)
                       ?? throw new InvalidOperationException($"Could not create RepositoryDesigner '{designerType.FullName}'.");
        return designer.CreateModel();
    }

    private static Type FindContextType(Assembly assembly, string? requestedContextName)
    {
        var contextTypes = GetLoadableTypes(assembly)
            .Where(static type =>
                !type.IsAbstract &&
                !type.ContainsGenericParameters &&
                typeof(DbContext).IsAssignableFrom(type))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(requestedContextName))
        {
            var selected = contextTypes.FirstOrDefault(type =>
                string.Equals(type.FullName, requestedContextName, StringComparison.Ordinal) ||
                string.Equals(type.Name, requestedContextName, StringComparison.Ordinal));
            return selected ?? throw new InvalidOperationException(
                $"DbContext '{requestedContextName}' was not found. Available contexts: {FormatContextNames(contextTypes)}.");
        }

        return contextTypes.Length switch
        {
            0 => throw new InvalidOperationException("The target assembly does not contain a concrete DbContext."),
            1 => contextTypes[0],
            _ => throw new InvalidOperationException(
                $"The target assembly contains multiple DbContexts. Use --context to select one: {FormatContextNames(contextTypes)}."),
        };
    }

    private static DbContext CreateContext(Type contextType)
    {
        var parameterless = contextType.GetConstructor(Type.EmptyTypes);
        if (parameterless is not null)
        {
            return (DbContext)parameterless.Invoke(null);
        }

        var optionsType = typeof(DbContextOptions<>).MakeGenericType(contextType);
        var constructor = contextType.GetConstructor([optionsType]);
        if (constructor is null)
        {
            throw new InvalidOperationException(
                $"{contextType.FullName} needs either a parameterless constructor or a constructor accepting DbContextOptions<{contextType.Name}>.");
        }

        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder?)Activator.CreateInstance(builderType)
                      ?? throw new InvalidOperationException($"Could not create options for {contextType.FullName}.");
        builder.UseInMemoryDatabase("RepoGenRepositoryGeneration");
        var options = builderType.GetProperty(
                              nameof(DbContextOptionsBuilder.Options),
                              BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)?
                          .GetValue(builder)
                      ?? throw new InvalidOperationException($"Could not read options for {contextType.FullName}.");

        return (DbContext)constructor.Invoke([options]);
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var details = string.Join(
                Environment.NewLine,
                exception.LoaderExceptions.Where(static item => item is not null).Select(static item => item!.Message));
            throw new InvalidOperationException(
                $"Some target-project types could not be loaded:{Environment.NewLine}{details}",
                exception);
        }
    }

    private static string FormatContextNames(IEnumerable<Type> contextTypes) =>
        string.Join(", ", contextTypes.Select(static type => type.FullName));
}