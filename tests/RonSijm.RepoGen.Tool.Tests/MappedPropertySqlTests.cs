using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.SqlServer;
using static RonSijm.RepoGen.Tool.Tests.MappedPropertyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertySqlTests
{
    [SqlServerFixtureFact]
    public async Task NativeCommandsBindShadowRowversionAndReturnItsNewDatabaseValue()
    {
        await using var fixture = await ReturningShadowSqlFixture.CreateAsync(); await using var context = fixture.Context();
        var row = new ReturningShadowRow { TenantId = 1, Id = 1, Text = "first", Quantity = 1 }; context.Add(row); await context.SaveChangesAsync(); context.ChangeTracker.Clear();
        var token = await context.Rows.Select(entity => EF.Property<byte[]>(entity, "Fingerprint")).SingleAsync();
        var builder = Builder().Configure(options => options.UseReturningWrites<SqlServerReturningWriter>());
        var entity = builder.Entity<ReturningShadowRow>();
        entity.HasCommand("Rename").Where<SqlReturningRequest>((note, input) => note.Id == 1)
            .Update(setters => setters.SetProperty<string?, SqlReturningRequest>(note => note.Text, (note, input) => input.Text!))
            .ExpectVersion<byte[], SqlReturningRequest>("Fingerprint", input => input.Version)
            .ReturnsMany(note => EF.Property<byte[]>(note, "Fingerprint")).IncludeShadowProperties();
        entity.HasCommand("Consume").Where<SqlReturningRequest>((note, input) => note.Id == 1).Delete()
            .ExpectVersion<byte[], SqlReturningRequest>("Fingerprint", input => input.Version)
            .ReturnsMany(note => EF.Property<byte[]>(note, "Fingerprint")).IncludeShadowProperties();
        var assembly = ImmediateTestHarness.CompileProduction(context, builder);
        var operations = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.ReturningShadowRowRepositoryOperations")!, context, new SqlServerReturningWriter())!;
        Task<MutationResult<byte[]>> Call(string name, byte[] version) => ReturningWriteTests.Call<Task<MutationResult<byte[]>>>(operations, name, new SqlReturningRequest("", "renamed", 0, version), CancellationToken.None);
        var changed = await Call("RenameAsync", token); var next = changed.Items.Single(); next.Should().HaveCount(8).And.NotEqual(token);
        (await Call("RenameAsync", token)).AffectedRows.Should().Be(0);
        (await Call("ConsumeAsync", token)).AffectedRows.Should().Be(0);
        (await Call("ConsumeAsync", next)).Items.Single().Should().Equal(next);
        (await Call("ConsumeAsync", next)).AffectedRows.Should().Be(0); context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
