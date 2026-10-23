using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Testing;
using Xunit;
using static RonSijm.RepoGen.Tool.Tests.MemoryWriteTestHarness;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class InMemoryCommandTranslationGraphTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildPayloadKeysGuardsVisibilityAndNestedSavedReadsShareTheExplicitTranslations(bool stage)
    {
        var builder = SavedResultTestHarness.Builder(strict: false); Register(builder);
        var child = builder.Entity<MemoryCollectionChild>();
        child.HasVisibilityPolicy("Text", row => EF.Functions.Like(row.Text, "%"));
        var command = builder.Entity<MemoryCollectionParent>().HasCommand("TranslatedChildren")
            .Where<MemoryCollectionRequest>((row, request) => row.Id == TranslationCalls.Echo(request.Id) && row.OwnerId == request.OwnerId)
            .Patch(setters => setters.SetProperty<string, MemoryCollectionRequest>(row => row.Name, (row, request) => TranslatedWriteCalls.Normalize(request.Name))
                .SetProperty(row => row.Version, row => TranslationCalls.Shift(row.Version, 1))).AllowWrite(row => row.Version)
            .ExpectVersion<int, MemoryCollectionRequest>(row => row.Version, (row, request) => TranslationCalls.Echo(request.ExpectedVersion))
            .ReconcileCollection<MemoryCollectionChild, MemoryCollectionItem, string, MemoryCollectionRequest>(row => row.Children,
                request => (request.Items ?? Array.Empty<MemoryCollectionItem>()).Where(item => TranslationCalls.Positive(item.Text.Length)).ToArray(), row => row.Key, item => TranslationCalls.Echo(item.Key),
                children => children.MaximumItems(3).When<MemoryCollectionRequest>(request => TranslationCalls.Measure(request.Name) > 0)
                    .Add(item => new MemoryCollectionChild { Key = TranslationCalls.Echo(item.Key), Text = TranslatedWriteCalls.Normalize(item.Text), Version = TranslationCalls.Echo(1), Hidden = TranslationCalls.Echo(false) })
                    .Update(setters => setters.SetPropertyWhen(row => row.Text, (row, item) => TranslatedWriteCalls.Normalize(item.Text), item => TranslationCalls.Echo(item.Supplied))
                        .SetProperty(row => row.Version, row => TranslationCalls.Shift(row.Version, 1))).AllowWrite(row => row.Version)
                    .ExpectVersion(row => row.Version, (row, item) => TranslationCalls.Echo(item.ExpectedVersion))
                    .MissingItems(RepositoryCollectionRemovalBehavior.Delete)).SimulateCollectionWrites().UseInMemoryTranslations();
        if (stage) command.Stage();
        else command.Save().ReturnsSaved(row => row.Children.Where(item => !item.Hidden && EF.Functions.Like(item.Text, "%"))
            .OrderBy(item => item.Key).Select(item => TranslationCalls.Measure(item.Text)).ToList());
        using var context = MemoryCollectionTestHarness.Context();
        var model = RepositoryModelFactory.Create(context, typeof(MemoryCollectionContext).Assembly, design: builder.Build());
        model.Entities.Single(entity => entity.EntityName == nameof(MemoryCollectionParent)).Commands.Single(operation => operation.MethodName == "TranslatedChildrenAsync").InMemoryCommand!.Supported.Should().BeTrue();
        var (parent, _, data) = SavedResultTestHarness.Repositories(builder.Build());
        var input = new MemoryCollectionRequest(1, 10, " parent ", [new("a", " updated "), new("c", " created ")]);
        if (stage)
        {
            (await Invoke<CommandResult>(parent, "TranslatedChildrenAsync", input)).Status.Should().Be(CommandStatus.Staged);
            MemoryCollectionTestHarness.ChildStore(data).Find(1)!.Text.Should().Be("A");
            (await data.SaveChangesAsync()).Should().Be(4);
        }
        else (await Invoke<CommandResult<List<int>>>(parent, "TranslatedChildrenAsync", input)).Value.Should().Equal(7, 7);
        var rows = MemoryCollectionTestHarness.ChildStore(data).Snapshot();
        rows.Single(row => row.Key == "a").Text.Should().Be("UPDATED"); rows.Single(row => row.Key == "a").Version.Should().Be(2);
        rows.Single(row => row.Key == "c").ParentId.Should().Be(1);
        rows.Should().NotContain(row => row.Key == "b"); rows.Single(row => row.Key == "hidden").Hidden.Should().BeTrue();
        rows.Single(row => row.Key == "other").Text.Should().Be("other");
        MemoryCollectionTestHarness.ParentStore(data).Find(1)!.Name.Should().Be("PARENT");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CompositeJoinKeysPayloadsAndSavedSkipNavigationWorkWithoutChangingEndpointRows(bool mapped, bool stage)
    {
        var builder = LinkWriteTestHarness.Builder(mapped, strict: false); Register(builder);
        builder.Entity<LinkWriteRow>().HasVisibilityPolicy("Note", row => TranslationCalls.Measure(row.Note) > 0);
        var command = builder.Entity<LinkWriteOwner>().HasCommand("TranslatedLinks")
            .Where<LinkWriteRequest>((row, request) => row.TenantId == request.TenantId && row.Id == TranslationCalls.Echo(request.OwnerId))
            .Patch(setters => setters.SetProperty(row => row.Version, row => TranslationCalls.Shift(row.Version, 1))).AllowWrite(row => row.Version)
            .ExpectVersion<int, LinkWriteRequest>(row => row.Version, (row, request) => TranslationCalls.Echo(request.ExpectedVersion))
            .ReconcileLinks<LinkWriteRow, LinkWriteItem, (string, string), LinkWriteRequest>(row => row.Links,
                request => (request.Items ?? Array.Empty<LinkWriteItem>()).Where(item => TranslationCalls.Positive(item.Note.Length)).ToArray(),
                row => new ValueTuple<string, string>(row.TargetRegion, row.TargetCode), item => new ValueTuple<string, string>(TranslationCalls.Echo(item.Region), TranslationCalls.Echo(item.Code)),
                links => links.MaximumItems(3).Add(item => new LinkWriteRow { TargetRegion = TranslationCalls.Echo(item.Region), TargetCode = TranslationCalls.Echo(item.Code), Note = TranslatedWriteCalls.Normalize(item.Note), Version = TranslationCalls.Echo(1), Hidden = TranslationCalls.Echo(false) })
                    .Update(setters => setters.SetProperty(row => row.Note, (row, item) => TranslatedWriteCalls.Normalize(item.Note))
                        .SetProperty(row => row.Version, row => TranslationCalls.Shift(row.Version, 1))).AllowWrite(row => row.Version)
                    .ExpectVersion(row => row.Version, (row, item) => TranslationCalls.Echo(item.ExpectedVersion))
                    .MissingItems(RepositoryCollectionRemovalBehavior.Delete)).SimulateCollectionWrites().UseInMemoryTranslations();
        if (stage) command.Stage();
        else command.Save().ReturnsSaved(row => row.Tags.Where(tag => !tag.Hidden && EF.Functions.Like(tag.Name, "%"))
            .OrderBy(tag => tag.Region).ThenBy(tag => tag.Code).Select(tag => TranslationCalls.Measure(tag.Name)).ToList());
        using var context = LinkWriteTestHarness.Context();
        var design = builder.Build(); var bundle = RepositoryGenerator.Generate(context, design);
        var assembly = ScriptedTestDoubleTestHarness.Compile(bundle);
        var fixture = Activator.CreateInstance(assembly.GetType(LinkWriteTestHarness.Namespace + ".TestDoubles.LinkWriteContextRepositoryFixture")!)!;
        var data = (InMemoryWriteDataContext)fixture.GetType().GetProperty("Data")!.GetValue(fixture)!;
        if (mapped)
        {
            LinkWriteTestHarness.OwnerStore(data).SeedRange(LinkWriteTestHarness.Owners().Select(row => new LinkWriteOwnerData(row.TenantId, row.Id, row.Name, row.Version, row.Hidden)));
            LinkWriteTestHarness.TargetStore(data).SeedRange(LinkWriteTestHarness.Targets().Select(row => new LinkWriteTargetData(row.Region, row.Code, row.Name, row.Hidden)));
            LinkWriteTestHarness.LinkStore(data).SeedRange(LinkWriteTestHarness.Links().Select(row => new LinkWriteRowData(row.OwnerTenantId, row.OwnerId, row.TargetRegion, row.TargetCode, row.Note, row.Version, row.Hidden)));
        }
        else
        {
            LinkWriteTestHarness.Store<LinkWriteOwner, (int, int)>(data, nameof(LinkWriteOwner)).SeedRange(LinkWriteTestHarness.Owners());
            LinkWriteTestHarness.Store<LinkWriteTarget, (string, string)>(data, nameof(LinkWriteTarget)).SeedRange(LinkWriteTestHarness.Targets());
            LinkWriteTestHarness.Store<LinkWriteRow, (int, int, string, string)>(data, nameof(LinkWriteRow)).SeedRange(LinkWriteTestHarness.Links());
        }
        var parent = Activator.CreateInstance(assembly.GetType(LinkWriteTestHarness.Namespace + ".TestDoubles.LinkWriteOwnerRepositoryTestDouble")!, fixture)!;
        var input = new LinkWriteRequest(1, 1, [new("eu", "a", " updated "), new("us", "a", " created ")]);
        await using (await data.BeginTransactionAsync())
        {
            if (stage)
            {
                (await Invoke<CommandResult>(parent, "TranslatedLinksAsync", input)).Status.Should().Be(CommandStatus.Staged);
                LinkWriteTestHarness.Rows(data, mapped).Should().HaveCount(4); (await data.SaveChangesAsync()).Should().Be(4);
            }
            else (await Invoke<CommandResult<List<int>>>(parent, "TranslatedLinksAsync", input)).Value.Should().Equal(1, 4);
            var rows = LinkWriteTestHarness.Rows(data, mapped);
            rows.Single(row => row.OwnerTenantId == 1 && row.TargetRegion == "eu" && row.TargetCode == "a").Note.Should().Be("UPDATED");
            rows.Single(row => row.OwnerTenantId == 1 && row.TargetRegion == "us").Note.Should().Be("CREATED");
            rows.Should().Contain(row => row.OwnerTenantId == 2 && row.Note == "other").And.Contain(row => row.Hidden);
            (mapped ? LinkWriteTestHarness.TargetStore(data).Count : LinkWriteTestHarness.Store<LinkWriteTarget, (string, string)>(data, nameof(LinkWriteTarget)).Count).Should().Be(4);
        }
        LinkWriteTestHarness.Rows(data, mapped).Should().HaveCount(4).And.NotContain(row => row.Note == "UPDATED" || row.Note == "CREATED");
    }

    private static void Register(RepositoryModelBuilder builder)
    {
        InMemoryCommandTranslationTestHarness.Register(builder);
        builder.HasInMemoryTranslation<string, string>("StringEcho", value => TranslationCalls.Echo(value), value => value);
    }
}
