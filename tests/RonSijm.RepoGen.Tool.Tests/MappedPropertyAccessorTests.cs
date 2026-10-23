using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Design;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MappedPropertyTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedPropertyAccessorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedShadowLookupsWorkWithCombinedAndCqrsSelectorsWithoutBroadeningInference(bool cqrs)
    {
        await using var fixture = await CreateAsync(); var builder = Builder(closed: false);
        if (cqrs) builder.Configure(options => options.UseCqrsSelectors());
        var entity = builder.Entity<MappedPropertyNote>();
        entity.HasAccessor<string>("Code");
        var model = RepositoryModelFactory.Create(fixture.Context, typeof(MappedPropertyNote).Assembly, generateSelectors: true, design: builder.Build());
        model.Entities.Single().Lookups.Single(lookup => lookup.MethodName == "ByCode").IsUnique.Should().BeTrue();
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedPropertyNoteRepository")!, fixture.Context)!;
        if (cqrs) repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedPropertyNoteReadRepository")!, repository)!;
        var selector = repository.GetType().GetMethod("ByCode")!.Invoke(repository, ["one"])!;
        var result = await ReadSelector<Task<MappedPropertyNote?>>(selector, "AsEntityAsync");
        result!.Id.Should().Be(1);
        var hidden = repository.GetType().GetMethod("ByCode")!.Invoke(repository, ["hidden"])!;
        (await ReadSelector<Task<MappedPropertyNote?>>(hidden, "AsEntityAsync")).Should().BeNull();
        var missing = repository.GetType().GetMethod("ByCode")!.Invoke(repository, ["'; DROP TABLE ignored; --"])!;
        (await ReadSelector<Task<MappedPropertyNote?>>(missing, "AsEntityAsync")).Should().BeNull();
        (await fixture.Context.Notes.IgnoreQueryFilters().CountAsync()).Should().Be(3);
        var plain = Builder(closed: false); plain.Entity<MappedPropertyNote>();
        RepositoryModelFactory.Create(fixture.Context, typeof(MappedPropertyNote).Assembly, true, plain.Build()).Entities.Single().Lookups.Should().NotContain(lookup => lookup.MethodName == "ByCode");
    }

    [Theory]
    [InlineData("token")]
    [InlineData("converter")]
    [InlineData("clr")]
    public async Task ExplicitNamesAdaptersAndNonUniqueLookupsUseEfMappedReads(string mode)
    {
        await using var fixture = await CreateAsync(); var builder = Builder(closed: false); var entity = builder.Entity<MappedPropertyNote>();
        object input;
        if (mode == "token") { entity.HasAccessor<int>(MappedPropertyContext.Token).HasMethodName("ByValue").IsUnique(false); input = 1; }
        else if (mode == "converter") { entity.HasAccessor<ReturningState>("State").UseValueConverter<string>().HasMethodName("ByValue").IsUnique(false); input = "Ready"; }
        else { entity.HasAccessor<string>(nameof(MappedPropertyNote.Title)).HasMethodName("ByValue").IsUnique(false); input = "title1"; }
        var model = RepositoryModelFactory.Create(fixture.Context, typeof(MappedPropertyNote).Assembly, true, builder.Build());
        var assembly = ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        var repository = Activator.CreateInstance(assembly.GetType(Namespace + ".Persistence.MappedPropertyNoteRepository")!, fixture.Context)!;
        var selector = repository.GetType().GetMethod("ByValue")!.Invoke(repository, [input])!;
        var rows = await ReadSelector<Task<IReadOnlyList<MappedPropertyNote>>>(selector, "AsEntitiesAsync");
        rows.Select(row => row.Id).Should().BeEquivalentTo<int>(mode == "token" ? [1, 2] : [1]);
        model.Entities.Single().Lookups.Single(lookup => lookup.MethodName == "ByValue").Properties.Single().MappedProperty.Should().NotBeNull();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrongType")]
    [InlineData("badName")]
    [InlineData("keyword")]
    public void InvalidBindingsFailBeforeOpeningTheDatabase(string scenario)
    {
        using var context = Empty(); var builder = Builder(closed: false); var entity = builder.Entity<MappedPropertyNote>();
        if (scenario == "missing") entity.HasAccessor<string>("Missing");
        else if (scenario == "wrongType") entity.HasAccessor<int>("Code");
        else if (scenario == "badName") entity.HasAccessor<int>(MappedPropertyContext.Token);
        else entity.HasAccessor<string>("Code").HasMethodName("class");
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1710:*");
        context.Database.GetDbConnection().State.Should().Be(System.Data.ConnectionState.Closed);
    }
}
