// Paired with ../Model/NotesContext.cs by ../../GenerationRunner/Program.cs.
// Output: ../Generated/Repositories.g.cs (INoteRepository); see ../README.md for the mapping.
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

/// <summary>The minimum configuration: inferred lookups and ordinary CRUD, without advanced helpers.</summary>
public sealed class SimpleRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder) =>
        modelBuilder.Configure(options => options.UseSimpleDefaults());
}
