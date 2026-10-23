using System.Reflection;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryModelBuilder
{
    private static readonly MethodInfo ApplyEntityConfigurationMethod = typeof(RepositoryModelBuilder).GetMethods()
        .Single(method => method.Name == nameof(ApplyConfiguration) && method.IsGenericMethodDefinition);

    /// <summary>Discovers configuration modules in one explicitly selected assembly.</summary>
    /// <remarks>
    /// Applies concrete, closed implementations of IRepositoryEntityConfiguration or IRepositoryModelConfiguration
    /// in ordinal full-type-name order. The optional predicate selects modules before construction.
    /// Non-public types and parameterless constructors are supported. Model configuration runs before entity
    /// configuration on a type implementing both contracts; entity contracts run in ordinal entity-type-name order.
    /// Discovery is explicit and runs on every call; modules are not silently deduplicated.
    /// </remarks>
    public RepositoryModelBuilder ApplyConfigurationsFromAssembly(Assembly assembly, Func<Type, bool>? predicate = null)
    {
        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            throw new InvalidOperationException(
                $"RGD1006: Cannot discover repository configurations in '{assembly.FullName}'. " +
                string.Join("; ", exception.LoaderExceptions.Where(error => error is not null).Select(error => error!.Message)), exception);
        }

        foreach (var type in types.Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters)
                     .Where(type => typeof(IRepositoryModelConfiguration).IsAssignableFrom(type) || EntityConfigurationContracts(type).Any())
                     .Where(type => predicate is null || predicate(type))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, Type.EmptyTypes, modifiers: null);
            if (constructor is null)
            {
                throw new InvalidOperationException($"RGD1006: Repository configuration '{type.FullName}' requires a parameterless constructor for assembly discovery. " +
                    "Provide one, exclude the type with the predicate, or register an instance with ApplyConfiguration.");
            }

            try
            {
                var configuration = constructor.Invoke(Array.Empty<object>());
                if (configuration is IRepositoryModelConfiguration)
                {
                    typeof(IRepositoryModelConfiguration).GetMethod(nameof(IRepositoryModelConfiguration.Configure))!
                        .Invoke(configuration, new object[] { this });
                }

                foreach (var contract in EntityConfigurationContracts(type).OrderBy(contract => contract.GenericTypeArguments[0].FullName, StringComparer.Ordinal))
                {
                    ApplyEntityConfigurationMethod.MakeGenericMethod(contract.GenericTypeArguments[0]).Invoke(this, new[] { configuration });
                }
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException($"RGD1006: Repository configuration '{type.FullName}' failed during assembly discovery. " +
                    (exception.InnerException ?? exception).Message,
                    exception.InnerException ?? exception);
            }
        }

        return this;
    }

    private static IEnumerable<Type> EntityConfigurationContracts(Type type) => type.GetInterfaces()
        .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IRepositoryEntityConfiguration<>));
}
