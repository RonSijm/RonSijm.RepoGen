using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedPropertyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyBatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchesCheckEachShadowTokenWithoutPartiallySavingOnConflict(bool delete)
    {
        await using var fixture = await CreateAsync(); var builder = Builder(); var entity = builder.Entity<MappedPropertyNote>();
        var command = entity.HasCommand("Change");
        if (delete) command.DeleteMany<MappedPropertyItem, int, MappedPropertyBatch>(input => input.Items, row => row.Id, item => item.Id,
            items => items.ExpectVersion<int>(MappedPropertyContext.Token, item => item.Version), 10);
        else command.PatchMany<MappedPropertyItem, int, MappedPropertyBatch>(input => input.Items, row => row.Id, item => item.Id,
            items => items.SetProperty(row => row.Title, (row, item) => item.Title).SetProperty(row => row.Counter, row => row.Counter + 1)
                .ExpectVersion<int>(MappedPropertyContext.Token, item => item.Version), 10);
        command.Save(); var operations = Operations(fixture.Context, builder);
        var failed = await ReturningWriteTests.Call<Task<CommandResult>>(operations, "ChangeAsync", new MappedPropertyBatch([new(1, "first", 1), new(2, "second", 99)]), CancellationToken.None);
        failed.Succeeded.Should().BeFalse();
        (await fixture.Context.Notes.AsNoTracking().ToListAsync()).Should().OnlyContain(row => row.Counter == 0);
        var saved = await ReturningWriteTests.Call<Task<CommandResult>>(operations, "ChangeAsync", new MappedPropertyBatch([new(1, "first", 1), new(2, "second", 1)]), CancellationToken.None);
        saved.Succeeded.Should().BeTrue();
        if (delete) (await fixture.Context.Notes.CountAsync()).Should().Be(0);
        else (await fixture.Context.Notes.AsNoTracking().Select(row => EF.Property<int>(row, MappedPropertyContext.Token)).ToListAsync()).Should().Equal(2, 2);
    }
}
