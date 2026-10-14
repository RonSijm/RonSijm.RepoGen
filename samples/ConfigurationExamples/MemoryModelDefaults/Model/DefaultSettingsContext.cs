using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.MemoryModelDefaults;

namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelDefaults;

public sealed class DefaultSettingsContext(DbContextOptions<DefaultSettingsContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<StoredDefaultSetting>(); entity.Property(row => row.Id).ValueGeneratedNever();
        entity.Property(row => row.Credits).HasDefaultValue(5).HasSentinel(-1);
        entity.Property(row => row.Enabled).HasDefaultValue(true);
        entity.Property(row => row.Level).HasDefaultValue(DefaultSettingLevel.Standard).HasSentinel(DefaultSettingLevel.Unspecified);
        entity.Property(row => row.Origin).HasDefaultValue("model");
    }
}
