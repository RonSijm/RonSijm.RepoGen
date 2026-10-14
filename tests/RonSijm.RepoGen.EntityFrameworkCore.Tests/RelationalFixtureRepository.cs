namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

public sealed class RelationalFixtureRepository(RelationalFixtureContext context)
{
    public RelationalFixtureContext Context { get; } = context;
}
