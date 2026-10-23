namespace RonSijm.RepoGen.Design;

/// <summary>Declares a provider requirement on a registered query provider or optional infrastructure capability.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class RepositoryProviderAttribute : Attribute
{
    public RepositoryProviderAttribute(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("An EF provider name is required.", nameof(providerName));
        }

        ProviderName = providerName;
    }

    public string ProviderName { get; }
}
