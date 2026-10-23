using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class MappedReadContext(DbContextOptions<MappedReadContext> options) : DbContext(options)
{
    public const string Tag = "source.tag \" alias";
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var item = modelBuilder.Entity<MappedReadItem>();
        item.Property(row => row.Id).ValueGeneratedNever();
        item.Property(row => row.Name).UsePropertyAccessMode(PropertyAccessMode.Property);
        item.Property(row => row.Caption).HasField("caption").UsePropertyAccessMode(PropertyAccessMode.Field);
        item.Property<int>(nameof(MappedReadItem.PublicRank)).HasField(nameof(MappedReadItem.PublicRank));
        item.Property<int>("audit").HasField("audit").IsConcurrencyToken();
        item.Property<bool>("archived").HasField("archived");
        item.Property<MappedReadState?>("state").HasField("state").HasConversion<string>();
        item.Property<byte[]?>("fingerprint").HasField("fingerprint");
        item.Property<int>("fixedValue").HasField("fixedValue");
        item.IndexerProperty<string>(Tag).IsRequired();
        item.HasQueryFilter(row => !EF.Property<bool>(row, "archived") && row.PublicRank >= 0);
        item.HasOne(row => row.Folder).WithMany(folder => folder.Items).HasForeignKey(row => row.FolderId);
        var folder = modelBuilder.Entity<MappedReadFolder>();
        folder.Property(row => row.Id).ValueGeneratedNever();
        folder.Property<bool>("hidden").HasField("hidden");
        folder.HasQueryFilter(row => !EF.Property<bool>(row, "hidden"));
    }
}
