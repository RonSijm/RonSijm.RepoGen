using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedPropertyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyRaceTests
{
    [Fact]
    public async Task CompetingSetBasedShadowVersionUpdatesAndConsumesHaveOneWinner()
    {
        var file = Path.Combine(Path.GetTempPath(), "repogen-mapped-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var options = new DbContextOptionsBuilder<MappedPropertyContext>().UseSqlite("Data Source=" + file + ";Pooling=False;Default Timeout=15").Options;
            var builder = Builder(); Versioned(builder, "Rename");
            builder.Entity<MappedPropertyNote>().HasCommand("Consume").Where<MappedPropertyInput>((row, input) => row.Id == input.Id).Delete()
                .ExpectVersion<int, MappedPropertyInput>(MappedPropertyContext.Token, input => input.Version);
            System.Reflection.Assembly assembly;
            await using (var seed = new MappedPropertyContext(options))
            {
                await seed.Database.EnsureCreatedAsync(); var row = new MappedPropertyNote { Id = 1, Title = "initial" };
                seed.Add(row); seed.Entry(row).Property("Code").CurrentValue = "one"; await seed.SaveChangesAsync();
                assembly = ImmediateTestHarness.CompileProduction(seed, builder);
            }
            (await Race("RenameAsync", 1)).Sum().Should().Be(1);
            (await Race("ConsumeAsync", 2)).Sum().Should().Be(1);
            await using var verify = new MappedPropertyContext(options); (await verify.Notes.CountAsync()).Should().Be(0);
            Task<int[]> Race(string method, int token) => Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                await using var context = new MappedPropertyContext(options);
                var operations = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedPropertyNoteRepositoryOperations")!, context)!;
                return await ReturningWriteTests.Call<Task<int>>(operations, method, new MappedPropertyInput(1, token), CancellationToken.None);
            })));
        }
        finally { File.Delete(file); }
    }
}
