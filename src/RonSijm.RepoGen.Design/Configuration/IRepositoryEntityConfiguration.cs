namespace RonSijm.RepoGen.Design;

/// <summary>Configures one entity without requiring a monolithic designer class.</summary>
public interface IRepositoryEntityConfiguration<TEntity> where TEntity : class
{
    void Configure(RepositoryEntityBuilder<TEntity> entity);
}
