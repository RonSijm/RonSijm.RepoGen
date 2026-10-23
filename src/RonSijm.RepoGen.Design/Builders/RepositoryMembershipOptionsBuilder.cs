namespace RonSijm.RepoGen.Design;

/// <summary>Optional input handling for a designer-defined membership predicate. No database strategy is implied.</summary>
public sealed class RepositoryMembershipOptionsBuilder
{
    private RepositoryMembershipInputBehavior nullInput = RepositoryMembershipInputBehavior.Reject;
    private RepositoryMembershipInputBehavior emptyInput;
    private RepositoryMembershipNullValueBehavior nullValues;
    private bool distinct;
    private int? maximumCount;

    public RepositoryMembershipOptionsBuilder NullInput(RepositoryMembershipInputBehavior behavior)
    {
        Validate(behavior);
        nullInput = behavior;
        return this;
    }

    public RepositoryMembershipOptionsBuilder EmptyInput(RepositoryMembershipInputBehavior behavior)
    {
        Validate(behavior);
        emptyInput = behavior;
        return this;
    }

    public RepositoryMembershipOptionsBuilder NullValues(RepositoryMembershipNullValueBehavior behavior)
    {
        if (behavior < RepositoryMembershipNullValueBehavior.Include || behavior > RepositoryMembershipNullValueBehavior.Reject)
        {
            throw new ArgumentOutOfRangeException(nameof(behavior));
        }

        nullValues = behavior;
        return this;
    }

    /// <summary>Removes duplicates using the CLR default equality comparer, preserving first-occurrence order.</summary>
    public RepositoryMembershipOptionsBuilder Distinct(bool enabled = true)
    {
        distinct = enabled;
        return this;
    }

    /// <summary>Limits raw enumerated values, before deduplication/null removal. Null disables the limit.</summary>
    public RepositoryMembershipOptionsBuilder MaximumCount(int? count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        maximumCount = count;
        return this;
    }

    internal RepositoryMembershipOptions Build() => new(nullInput, emptyInput, nullValues, distinct, maximumCount);

    private static void Validate(RepositoryMembershipInputBehavior behavior)
    {
        if (behavior < RepositoryMembershipInputBehavior.EmptySet || behavior > RepositoryMembershipInputBehavior.Reject)
        {
            throw new ArgumentOutOfRangeException(nameof(behavior));
        }
    }
}
