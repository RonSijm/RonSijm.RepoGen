using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelDefaults;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelDefaults;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class DefaultSettingsRepositoryDesigner : RepositoryDesigner
{
    private static readonly string[] Creates = ["Create", "Stage"];
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryModelDefaults")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryModelDefaults")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryModelDefaults")
            .UseInMemoryTestStores().UseInMemoryTestQueries().UseInMemoryTestCommands().RequirePortableTestCommands());
        modelBuilder.Entity<StoredDefaultSetting>(entity =>
        {
            entity.BindReadPort<IDefaultSettingReader>().BindWritePort<IDefaultSettingWriter>();
            entity.HasInMemoryStore<DefaultSettingSeed, int>(row => row.Id, row => new(row.Id, row.Credits, row.Enabled, row.Level, row.Origin))
                .MapInMemoryEntity<DefaultSettingSeed>(row => new StoredDefaultSetting { Id = row.Id, Credits = row.Credits, Enabled = row.Enabled, Level = row.Level, Origin = row.Origin })
                .MapInMemoryWrites<DefaultSettingSeed>((row, saved) => new(row.Id, row.Credits, row.Enabled, row.Level, row.Origin))
                .UseInMemoryModelDefaults(); // Off by default. Reuse constants/sentinels from the context, not SQL expressions.
            entity.HasQuery("Rows").OrderBy(row => row.Id).Select(row => new DefaultSettingSeed(row.Id, row.Credits, row.Enabled, row.Level, row.Origin)).List();
            foreach (var name in Creates)
            {
                var command = entity.HasCommand(name).Create<DefaultSettingSeed>(request => new StoredDefaultSetting
                    { Id = request.Id, Credits = request.Credits, Enabled = request.Enabled, Level = request.Level, Origin = request.Origin });
                Permit(command); // Defaults do not loosen production write permissions.
                if (name == "Stage") command.Stage();
                else command.Save().ReturnsSaved(row => new DefaultSettingSeed(row.Id, row.Credits, row.Enabled, row.Level, row.Origin));
            }
            var batch = entity.HasCommand("CreateMany").CreateMany<DefaultSettingSeed, IReadOnlyList<DefaultSettingSeed>>(requests => requests,
                item => new StoredDefaultSetting { Id = item.Id, Credits = item.Credits, Enabled = item.Enabled, Level = item.Level, Origin = item.Origin }, maximumItems: 4);
            Permit(batch); batch.Save().ReturnsSaved(row => new DefaultSettingSeed(row.Id, row.Credits, row.Enabled, row.Level, row.Origin));
            entity.HasCommand("ResetCredits").Where<int>((row, id) => row.Id == id).Patch(setters => setters.SetProperty(row => row.Credits, -1))
                .AllowWrite(row => row.Credits).Save(); // Insert defaults do not run on update.
        });
    }
    private static void Permit(RepositoryCommandBuilder<StoredDefaultSetting> command) => command.AllowWrite(row => row.Id).AllowWrite(row => row.Credits)
        .AllowWrite(row => row.Enabled).AllowWrite(row => row.Level).AllowWrite(row => row.Origin);
}
