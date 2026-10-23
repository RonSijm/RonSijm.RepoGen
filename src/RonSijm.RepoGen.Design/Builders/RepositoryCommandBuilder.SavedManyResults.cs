using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed partial class RepositoryCommandBuilder<TEntity> where TEntity : class
{
    /// <summary>Reads fresh DTOs by the directly updated saved primary keys before native Update commits.</summary>
    /// <remarks>Requires a native returning writer and a positive explicit result bound. Oversized/missing
    /// results fail within the owned transaction or borrowed transaction savepoint. No shared Save is performed.
    /// Delete is excluded: deleted roots cannot be freshly queried. Item order is unspecified.</remarks>
    public RepositoryCommandBuilder<TEntity> ReturnsSavedMany<TResult>(Expression<Func<TEntity, TResult>> projection, int maximumRows)
    {
        ValidateSavedResultBound(maximumRows);
        ReturnsMany(projection);
        design.ResultFromSaved = true;
        design.SavedResultMaximumRows = maximumRows;
        return this;
    }

    /// <summary>Reads bounded fresh native Update results with explicitly bound request values.</summary>
    public RepositoryCommandBuilder<TEntity> ReturnsSavedMany<TResult, TRequest>(Expression<Func<TEntity, TRequest, TResult>> projection, int maximumRows)
    {
        ValidateSavedResultBound(maximumRows);
        ReturnsMany(projection);
        design.ResultFromSaved = true;
        design.SavedResultMaximumRows = maximumRows;
        return this;
    }

    private static void ValidateSavedResultBound(int maximumRows)
    {
        if (maximumRows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRows), "Fresh native results require a positive explicit row bound.");
        }
    }
}
