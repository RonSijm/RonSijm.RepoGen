namespace RonSijm.RepoGen.Design;

internal sealed class RepositoryLoadingConfiguration
{
    private readonly List<RepositoryLoadProfileDesign> resolvedProfiles = new();
    internal List<RepositoryIncludeDesign> IncludesList { get; } = new();
    internal List<RepositoryLoadProfileReference> ProfileReferences { get; } = new();
    internal IReadOnlyList<RepositoryIncludeDesign> Includes => IncludesList.Concat(resolvedProfiles.SelectMany(profile => profile.Includes)).ToArray();
    internal IReadOnlyList<string> ProfileNames => ProfileReferences.Select(reference => reference.Name).ToArray();

    internal void Use(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A load profile name is required.", nameof(name));
        }

        ProfileReferences.Add(new RepositoryLoadProfileReference(name.Trim()));
    }

    internal void Use<TEntity>(RepositoryLoadProfile<TEntity> profile) where TEntity : class
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        ProfileReferences.Add(new RepositoryLoadProfileReference(profile.Name, profile.Design));
    }

    internal void Resolve(RepositoryEntityDesign entity, string owner)
    {
        resolvedProfiles.Clear();
        foreach (var reference in ProfileReferences)
        {
            var profile = entity.LoadProfiles.SingleOrDefault(profile => profile.Name == reference.Name);
            if (profile is null || reference.Profile is not null && !ReferenceEquals(profile, reference.Profile))
            {
                throw new InvalidOperationException($"RGD1404: '{owner}' references load profile '{reference.Name}' that is not registered for entity '{entity.EntityType.FullName}' in this designer.");
            }

            if (!resolvedProfiles.Contains(profile))
            {
                resolvedProfiles.Add(profile);
            }
        }
    }
}
