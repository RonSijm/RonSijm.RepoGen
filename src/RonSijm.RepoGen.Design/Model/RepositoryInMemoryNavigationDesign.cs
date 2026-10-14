using System.Reflection;

namespace RonSijm.RepoGen.Design;

/// <summary>An explicit test-read relationship; its keys are resolved from the final EF model.</summary>
public sealed class RepositoryInMemoryNavigationDesign
{
    internal RepositoryInMemoryNavigationDesign(MemberInfo member, bool preserveMissingTargets = false)
    {
        Member = member;
        PreserveMissingTargets = preserveMissingTargets;
    }
    public string Name => Member.Name;
    public MemberInfo Member { get; }
    public bool PreserveMissingTargets { get; }
}
