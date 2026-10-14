using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryPartitions;

public sealed class PartitionSetting
{
    public string Code { get; set; } = "";
    public string? Value { get; set; }
}

public sealed class PartitionDirectoryContext(DbContextOptions<PartitionDirectoryContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<PartitionSetting>().HasKey(setting => setting.Code);
}
