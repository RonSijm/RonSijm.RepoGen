// See ../README.md for this designer's exact repository, projection and manifest outputs.
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts;
using RonSijm.RepoGen.ConfigurationExamples.Models.MappedAccessors;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class AliasDirectoryRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().UseCqrsSelectors().GenerateOnlyConfiguredEntities()
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.MappedAccessors.Persistence"));
        modelBuilder.Entity<AliasEntry>(entity =>
        {
            entity.WriteEnabled = false;
            entity.ProjectionEnabled = false;
            // Neither property exists on AliasEntry. SearchCode inherits unique lookup semantics from EF metadata.
            entity.HasAccessor<string>("SearchCode");
            entity.HasAccessor<AliasState>("State").UseValueConverter<string>().HasMethodName("ByState").IsUnique(false);
            entity.HasProjection(row => new AliasCard(row.Id, row.Name, EF.Property<string>(row, "SearchCode")));
        });
    }
}
