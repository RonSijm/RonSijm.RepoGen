using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Tool.Tests;

public class MappedKeyContext(DbContextOptions<MappedKeyContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var owner = modelBuilder.Entity<MappedKeyOwner>();
        owner.Property(row => row.Id).ValueGeneratedNever();
        owner.Property<int>("Scope");
        owner.Property(row => row.Code).HasField("code").UsePropertyAccessMode(PropertyAccessMode.Field).UseCollation("NOCASE");
        owner.HasAlternateKey("Scope", nameof(MappedKeyOwner.Code));
        owner.Property<bool>("Archived");
        owner.HasQueryFilter(row => !EF.Property<bool>(row, "Archived"));
        var child = modelBuilder.Entity<MappedKeyChild>();
        child.Property(row => row.Id).ValueGeneratedNever();
        child.Property<int?>("OwnerScope");
        child.IndexerProperty<string?>("OwnerCode");
        child.HasOne(row => row.Owner).WithMany(row => row.Children)
            .HasForeignKey("OwnerScope", "OwnerCode").HasPrincipalKey("Scope", nameof(MappedKeyOwner.Code)).IsRequired(false);
        child.HasQueryFilter(row => !row.Deleted);
        modelBuilder.Entity<MappedKeyTag>().Property(row => row.Id).ValueGeneratedNever();
        var link = modelBuilder.Entity<MappedKeyLink>();
        link.Property(row => row.Id).ValueGeneratedNever();
        link.Property<int?>("OwnerScope");
        link.Property<string?>("ownerCode").HasField("ownerCode");
        link.IndexerProperty<int?>("TagId");
        link.Property<bool>("Removed");
        link.HasQueryFilter(row => !EF.Property<bool>(row, "Removed"));
        owner.HasMany(row => row.Tags).WithMany(row => row.Owners).UsingEntity<MappedKeyLink>(
            right => right.HasOne<MappedKeyTag>().WithMany().HasForeignKey("TagId").IsRequired(false),
            left => left.HasOne<MappedKeyOwner>().WithMany().HasForeignKey("OwnerScope", "ownerCode")
                .HasPrincipalKey("Scope", nameof(MappedKeyOwner.Code)).IsRequired(false));
    }
}
