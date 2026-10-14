// Designer -> ../Model/PartitionDirectoryContext.cs -> ../Generated/TestDoubles.g.cs and shared contracts.
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryPartitions;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryPartitions;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class PartitionDirectoryRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.MemoryPartitions")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.MemoryPartitions")
            .UseKeyedContextLeases<PartitionKey>()
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.MemoryPartitions")
            .UseInMemoryTestStores().GenerateOnlyConfiguredEntities());

        modelBuilder.Entity<PartitionSetting>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<ISettingLookup>();
            entity.HasInMemoryStore<SettingSeed, string>(setting => setting.Code,
                setting => new SettingSeed { Code = setting.Code, Value = setting.Value });
            entity.HasProjection(setting => new SettingView(setting.Code, setting.Value));
            entity.HasQuery("Find").UseContextKey<SettingLookup, PartitionKey>(request => request.Partition)
                .Where<SettingLookup>((setting, request) => setting.Code == request.Code).ProjectTo<SettingView>().SingleOrDefault();
        });
    }
}
