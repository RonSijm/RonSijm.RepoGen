namespace RonSijm.RepoGen.Design;

public abstract class RepositoryDesigner
{
    public RepositoryDesign CreateModel()
        => CreateModel(configureDefaults: null);

    /// <summary>Applies generator-supplied defaults before the designer's explicit configuration.</summary>
    public RepositoryDesign CreateModel(Action<RepositoryGenerationOptionsBuilder>? configureDefaults)
    {
        var modelBuilder = new RepositoryModelBuilder();
        if (configureDefaults is not null)
        {
            modelBuilder.Configure(configureDefaults);
        }

        OnModelCreating(modelBuilder);
        return modelBuilder.Build();
    }

    protected abstract void OnModelCreating(RepositoryModelBuilder modelBuilder);
}
