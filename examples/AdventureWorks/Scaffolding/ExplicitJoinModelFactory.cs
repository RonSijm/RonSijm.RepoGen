// EF's default scaffolder hides pure junction tables behind skip navigations.
// Keep them as ordinary entities so RepoGen demonstrates every table, including composite keys.
#pragma warning disable EF1001 // The pinned EF 8 scaffolding extension points are internal APIs.
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;

namespace RonSijm.RepoGen.AdventureWorks.Scaffolding;

internal sealed class ExplicitJoinModelFactory(
    IOperationReporter reporter,
    ICandidateNamingService candidateNamingService,
    IPluralizer pluralizer,
    ICSharpUtilities cSharpUtilities,
    IScaffoldingTypeMapper scaffoldingTypeMapper,
    IModelRuntimeInitializer modelRuntimeInitializer)
    : RelationalScaffoldingModelFactory(reporter, candidateNamingService, pluralizer, cSharpUtilities,
        scaffoldingTypeMapper, modelRuntimeInitializer)
{
    protected override ModelBuilder VisitForeignKeys(ModelBuilder modelBuilder, IList<DatabaseForeignKey> foreignKeys)
    {
        foreach (var foreignKey in foreignKeys)
        {
            VisitForeignKey(modelBuilder, foreignKey);
        }

        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()).ToArray())
        {
            AddNavigationProperties(foreignKey);
        }

        return modelBuilder;
    }
}
