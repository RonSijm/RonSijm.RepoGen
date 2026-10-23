using System.Reflection;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryModelBuilder
{
    private static void ApplyOperationMethods<TEntity>(IRepositoryEntityConfiguration<TEntity> configuration,
        RepositoryEntityBuilder<TEntity> entity) where TEntity : class
    {
        var configurationType = configuration.GetType();
        var entityTypes = EntityConfigurationContracts(configurationType).Select(contract => contract.GenericTypeArguments[0]).ToArray();
        var methods = configurationType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => !method.IsSpecialName && typeof(IRepositoryOperationBuilder).IsAssignableFrom(method.ReturnType))
            .OrderBy(method => InheritanceDepth(method.DeclaringType))
            .ThenBy(method => method.MetadataToken);

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var parameterType = parameters.Length == 1 ? parameters[0].ParameterType : null;
            if (method.IsStatic || method.ContainsGenericParameters || parameterType is null ||
                !parameterType.IsGenericType || parameterType.GetGenericTypeDefinition() != typeof(RepositoryEntityBuilder<>) ||
                !entityTypes.Contains(parameterType.GenericTypeArguments[0]))
            {
                throw new InvalidOperationException($"RGD1007: Repository operation method '{configurationType.FullName}.{method.Name}' " +
                    "must be a public, non-generic instance method taking exactly one RepositoryEntityBuilder<TEntity> " +
                    "for an entity configured by this module. Make ordinary helpers private.");
            }

            // A module can implement several entity contracts. Each method runs only for its matching entity.
            if (parameterType != typeof(RepositoryEntityBuilder<TEntity>))
            {
                continue;
            }

            try
            {
                if (method.Invoke(configuration, new object[] { entity }) is null)
                {
                    throw new InvalidOperationException($"RGD1007: Repository operation method '{configurationType.FullName}.{method.Name}' " +
                        "returned null. Return the configured query or command builder.");
                }
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException($"RGD1007: Repository operation method '{configurationType.FullName}.{method.Name}' failed.",
                    exception.InnerException ?? exception);
            }
        }
    }

    private static int InheritanceDepth(Type? type)
    {
        var depth = 0;
        while (type is not null)
        {
            depth++;
            type = type.BaseType;
        }
        return depth;
    }
}
