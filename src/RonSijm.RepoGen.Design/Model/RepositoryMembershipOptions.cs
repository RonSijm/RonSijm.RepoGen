namespace RonSijm.RepoGen.Design;

public sealed class RepositoryMembershipOptions
{
    internal RepositoryMembershipOptions(RepositoryMembershipInputBehavior nullInput, RepositoryMembershipInputBehavior emptyInput,
        RepositoryMembershipNullValueBehavior nullValues, bool distinct, int? maximumCount)
    {
        NullInput = nullInput;
        EmptyInput = emptyInput;
        NullValues = nullValues;
        Distinct = distinct;
        MaximumCount = maximumCount;
    }

    public RepositoryMembershipInputBehavior NullInput { get; }
    public RepositoryMembershipInputBehavior EmptyInput { get; }
    public RepositoryMembershipNullValueBehavior NullValues { get; }
    public bool Distinct { get; }
    public int? MaximumCount { get; }
}
