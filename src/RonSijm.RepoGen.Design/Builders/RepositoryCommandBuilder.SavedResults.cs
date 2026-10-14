using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> where TEntity : class
{
    /// <summary>Projects fresh, untracked saved rows and related data after Create/CreateMany/Patch/PatchMany saves.</summary>
    /// <remarks>Requeries by the complete primary key, not the mutation predicate. Root visibility policies
    /// still apply. Related visibility must be explicit in the projection. This is a separate read, not
    /// an atomic returning statement; failures after saving throw RepositorySavedResultException.
    /// CreateMany/PatchMany read the saved keys together and return one value per input item in input order.
    /// Fixture navigation results require UseInMemoryTestQueries and declared HasInMemoryNavigation relationships.</remarks>
    public RepositoryCommandBuilder<TEntity> ReturnsSaved<TResult>(Expression<Func<TEntity, TResult>> projection)
    {
        Returns(projection);
        design.ResultFromSaved = true;
        return this;
    }

    /// <summary>Projects saved data with explicitly bound request values after Create/CreateMany/Patch/PatchMany saves.</summary>
    public RepositoryCommandBuilder<TEntity> ReturnsSaved<TResult, TRequest>(Expression<Func<TEntity, TRequest, TResult>> projection)
    {
        Returns(projection);
        design.ResultFromSaved = true;
        return this;
    }
}
