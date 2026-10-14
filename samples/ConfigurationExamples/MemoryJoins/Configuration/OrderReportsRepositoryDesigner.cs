using RonSijm.RepoGen.ConfigurationExamples.Contracts.OrderReports;
using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.ConfigurationExamples.Designers;

public sealed class OrderReportsRepositoryDesigner : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options => options.UseSimpleDefaults()
            .UseClosedRepositories("RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.OrderReports")
            .UseGeneratedNamespace("RonSijm.RepoGen.ConfigurationExamples.Persistence.OrderReports")
            .UseScriptedTestDoubles("RonSijm.RepoGen.ConfigurationExamples.TestDoubles.OrderReports")
            .UseInMemoryTestStores().UseInMemoryTestQueries().RequirePortableTestQueries().GenerateOnlyConfiguredEntities());
        modelBuilder.Entity<DirectoryAccount>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore(row => row.Id, row => new DirectoryAccount
                { Id = row.Id, Name = row.Name, Region = row.Region, Hidden = row.Hidden });
            entity.HasVisibilityPolicy("Accounts", row => !row.Hidden);
        });
        modelBuilder.Entity<OrderEntry>(entity =>
        {
            entity.WriteEnabled = false;
            entity.BindReadPort<IOrderReports>();
            entity.HasInMemoryStore(row => row.Id, row => new OrderEntry
                { Id = row.Id, AccountId = row.AccountId, Region = row.Region, Quantity = row.Quantity, Hidden = row.Hidden });
            entity.HasVisibilityPolicy("Orders", row => !row.Hidden);
            entity.HasQuery("Joined").From(sources => sources.Set<OrderEntry>().Join(sources.Set<DirectoryAccount>(),
                    order => new { Id = order.AccountId, order.Region }, account => new { account.Id, account.Region },
                    (order, account) => new OrderAccountRow { OrderId = order.Id, AccountName = account.Name, Quantity = order.Quantity }))
                .OrderBy(row => row.OrderId).List();
            entity.HasQuery("LeftJoined").From(sources => sources.Set<OrderEntry>()
                    .GroupJoin(sources.Set<DirectoryAccount>(), order => new { Id = order.AccountId, order.Region }, account => new { account.Id, account.Region },
                        (order, accounts) => new { order, accounts })
                    .SelectMany(pair => pair.accounts.DefaultIfEmpty(),
                        (pair, account) => new OrderAccountRow { OrderId = pair.order.Id, AccountName = account == null ? null : account.Name, Quantity = pair.order.Quantity }))
                .OrderBy(row => row.OrderId).List();
            entity.HasQuery("Unmatched").From(sources => sources.Set<OrderEntry>()
                    .Where(order => !sources.Set<DirectoryAccount>().Any(account => account.Id == order.AccountId && account.Region == order.Region))
                    .Select(order => order.Id)).OrderBy(id => id).List();
            entity.HasQuery("Totals").Validate<OrderReportCriteria>(criteria => criteria.MinimumQuantity >= 0 && criteria.MinimumCount >= 0,
                    "Report limits must be non-negative.")
                .From<OrderReportCriteria, OrderAccountRow>((sources, criteria) => sources.Set<OrderEntry>()
                    .Where(order => order.Quantity >= criteria.MinimumQuantity)
                    .Join(sources.Set<DirectoryAccount>(), order => new { Id = order.AccountId, order.Region }, account => new { account.Id, account.Region },
                        (order, account) => new OrderAccountRow { OrderId = order.Id, AccountName = account.Name, Quantity = order.Quantity }))
                .GroupBy(row => row.AccountName ?? "", group => new AccountOrderTotal { AccountName = group.Key, Count = group.Count(), Quantity = group.Sum(row => row.Quantity) })
                .Where<OrderReportCriteria>((row, criteria) => row.Count >= criteria.MinimumCount).OrderBy(row => row.AccountName).Paged();
            entity.HasQuery("Directory").From(sources => sources.Set<DirectoryAccount>().Select(account => account.Name)).OrderBy(name => name).List();
            entity.HasQuery("Identifiers").From(sources => sources.Set<OrderEntry>().Select(order => order.Id)
                    .Union(sources.Set<DirectoryAccount>().Select(account => account.Id))).OrderBy(id => id).List();
            entity.HasQuery("Stream").From(sources => sources.Set<OrderEntry>().Join(sources.Set<DirectoryAccount>(),
                    order => new { Id = order.AccountId, order.Region }, account => new { account.Id, account.Region },
                    (order, account) => new OrderAccountRow { OrderId = order.Id, AccountName = account.Name, Quantity = order.Quantity }))
                .OrderBy(row => row.OrderId).Stream();
        });
    }
}
