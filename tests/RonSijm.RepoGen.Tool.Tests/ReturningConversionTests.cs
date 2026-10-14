using System.Globalization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ReturningConversionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(ReturningState.Ready)]
    public async Task RequestBoundNullableEnumConversionsPreserveNullsAndCastReceiverPrecedence(ReturningState? state)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ConvertedReturningContext(new DbContextOptionsBuilder<ConvertedReturningContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Rows.Add(new ConvertedReturningRow { State = ReturningState.Pending });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Conversion.Contracts")
            .UseReturningWrites<SqliteReturningWriter>().GenerateOnlyConfiguredEntities());
        builder.Entity<ConvertedReturningRow>().HasCommand("State").Update(setters => setters.SetProperty<ReturningState?, ConvertedStateRequest>(row => row.State, (row, request) => request.State))
            .ReturnsMany(row => row.State);
        var assembly = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(context, typeof(ConvertedReturningRow).Assembly, true, builder.Build()), rejectWarnings: true);
        var operations = Activator.CreateInstance(assembly.GetType(typeof(ConvertedReturningRow).Namespace + ".ConvertedReturningRowRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var result = await ReturningWriteTests.Call<Task<MutationResult<ReturningState?>>>(operations, "StateAsync", new ConvertedStateRequest(state), CancellationToken.None);
        result.Items.Should().Equal(new ReturningState?[] { state });
        (await context.Rows.CountAsync(row => row.State == state)).Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("42")]
    public async Task NullableModelAndProviderTypesConvertInBothDirectionsAndRequestResultsStayBound(string? value)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ConvertedReturningContext(new DbContextOptionsBuilder<ConvertedReturningContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Rows.Add(new ConvertedReturningRow { Number = "1" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Conversion.Contracts")
            .UseReturningWrites<SqliteReturningWriter>().GenerateOnlyConfiguredEntities());
        builder.Entity<ConvertedReturningRow>().HasCommand("Set")
            .InputNullability<string>("value", input => input.Nullable())
            .Update(setters => setters.SetProperty<string?, string?>(row => row.Number, (row, value) => value)
                .SetProperty(row => row.State, (ReturningState?)ReturningState.Ready))
            .ReturnsMany<ConvertedReturningDto, string?>((row, value) => new ConvertedReturningDto(row.Number, row.State, value));
        var assembly = ClosedRepositoryTests.Compile(RepositoryModelFactory.Create(context, typeof(ConvertedReturningRow).Assembly, true, builder.Build()), rejectWarnings: true);
        var operations = Activator.CreateInstance(assembly.GetType(typeof(ConvertedReturningRow).Namespace + ".ConvertedReturningRowRepositoryOperations")!, context, new SqliteReturningWriter())!;
        var result = await ReturningWriteTests.Call<Task<MutationResult<ConvertedReturningDto>>>(operations, "SetAsync", value!, CancellationToken.None);
        result.AffectedRows.Should().Be(1);
        result.Items[0].Should().Be(new ConvertedReturningDto(value, ReturningState.Ready, value));
        (await context.Rows.CountAsync(row => row.State == ReturningState.Ready && row.Number == value)).Should().Be(1);
        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}

public sealed record ConvertedReturningDto(string? Number, ReturningState? State, string? Input);

public sealed record ConvertedStateRequest(ReturningState? State);

public sealed class ConvertedReturningRow
{
    public int Id { get; set; }
    public string? Number { get; set; }
    public ReturningState? State { get; set; }
}

public sealed class ConvertedReturningContext(DbContextOptions<ConvertedReturningContext> options) : DbContext(options)
{
    public DbSet<ConvertedReturningRow> Rows => Set<ConvertedReturningRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ConvertedReturningRow>();
        entity.Property(row => row.Number).HasConversion(number => int.Parse(number!, CultureInfo.InvariantCulture), number => number.ToString(CultureInfo.InvariantCulture));
        entity.Property(row => row.State).HasConversion<string>();
    }
}