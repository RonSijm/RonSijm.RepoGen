using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.AdventureWorks.Model;

public static class AdventureWorksDatabase
{
    public static AdventureWorksContext CreateContext(string? connectionString = null) => new(
        new DbContextOptionsBuilder<AdventureWorksContext>()
            .UseSqlServer(connectionString ?? "Server=(localdb)\\MSSQLLocalDB;Database=AdventureWorks;Integrated Security=true;TrustServerCertificate=true",
                options => options.UseHierarchyId().UseNetTopologySuite())
            .Options);
}
