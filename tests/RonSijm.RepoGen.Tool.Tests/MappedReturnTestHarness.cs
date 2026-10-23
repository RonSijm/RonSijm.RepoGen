using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;

namespace RonSijm.RepoGen.Tool.Tests;

internal static class MappedReturnTestHarness
{
    private static readonly Lazy<Assembly> Generated = new(() => Compile(Builder()));

    internal static RepositoryModelBuilder Builder(bool simulate = true, bool strict = true, bool queries = true, bool commands = true)
    {
        var builder = MappedStateTestHarness.Builder(queries: queries, commands: commands, strict: strict)
            .Configure(options => options.UseReturningWrites<SqliteReturningWriter>());
        var entity = builder.Entity<MappedStateNote>();
        foreach (var name in new[] { "RenameReturned", "ConsumeReturned", "RenameAllReturned", "ConsumeAllReturned", "FailUpdateReturned", "FailDeleteReturned" })
        {
            var command = entity.HasCommand(name);
            if (name is "RenameReturned" or "ConsumeReturned")
            {
                command.Where<MappedStateRequest>((row, request) => row.Id == request.Id)
                    .ExpectVersion<int, MappedStateRequest>("Revision", request => request.Version);
                if (name == "ConsumeReturned")
                {
                    command.ExpectVersion<int?, MappedStateRequest>("OptionalVersion", request => request.OptionalVersion)
                        .ExpectVersion<byte[]?, MappedStateRequest>("Stamp", request => request.Stamp);
                }
            }
            if (name.StartsWith("Consume", StringComparison.Ordinal) || name == "FailDeleteReturned")
            {
                command.Delete();
            }
            else
            {
                command.Update(setters => setters.SetProperty(row => row.Title, "returned")
                    .SetProperty(row => row.Counter, row => row.Counter + 1));
            }
            if (name.StartsWith("Fail", StringComparison.Ordinal))
            {
                command.ReturnsMany(row => new MappedReturnFailure(row.Id, EF.Property<int>(row, "Revision")));
            }
            else
            {
                command.ReturnsMany(row => new MappedStateCard(row.Id, row.Title, row.Counter, row.Caption,
                    EF.Property<int>(row, "audit"), EF.Property<int>(row, "Revision"), EF.Property<int?>(row, "OptionalVersion"),
                    EF.Property<byte[]?>(row, "Stamp")));
            }
            command.IncludeMappedProperties().SimulateImmediateWrites(simulate);
        }
        return builder;
    }

    internal static Assembly Compile(RepositoryModelBuilder builder)
    {
        using var context = MappedStateTestHarness.Context();
        return ScriptedTestDoubleTestHarness.Compile(RepositoryGenerator.Generate(context, builder.Build()));
    }

    internal static Assembly Assembly => Generated.Value;
}
