using RonSijm.RepoGen.Design;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class PortableSourceRepositoryDesigner(bool keyed = false, bool enabled = true) : RepositoryDesigner
{
    protected override void OnModelCreating(RepositoryModelBuilder modelBuilder)
    {
        modelBuilder.Configure(options =>
        {
            options.UseSimpleDefaults().UseClosedRepositories("Generated.Sources.Contracts")
                .UseGeneratedNamespace("Generated.Sources.Persistence").UseScriptedTestDoubles("Generated.Sources.TestDoubles")
                .UseInMemoryTestStores().UseInMemoryTestQueries(enabled).RequirePortableTestQueries(enabled).GenerateOnlyConfiguredEntities();
            if (keyed) options.UseKeyedContextLeases<string>();
        });
        modelBuilder.Entity<PortableSourceCustomer>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore(row => row.Id, row => new PortableSourceCustomer { Id = row.Id, Name = row.Name, Zone = row.Zone, Hidden = row.Hidden });
            entity.HasVisibilityPolicy("Customers", row => !row.Hidden);
            // A store-only participating entity needs no generated query operations.
        });
        modelBuilder.Entity<PortableSourceOrder>(entity =>
        {
            entity.WriteEnabled = false;
            entity.HasInMemoryStore(row => row.Id, row => new PortableSourceOrder { Id = row.Id, CustomerId = row.CustomerId, Zone = row.Zone, Amount = row.Amount, Hidden = row.Hidden });
            entity.HasVisibilityPolicy("Orders", row => !row.Hidden);
            RepositoryQueryBuilder<PortableSourceOrder> Query(string name)
            {
                var query = entity.HasQuery(name);
                if (keyed) query.UseContextKey<PortableSourceRequest, string>(request => request.Partition);
                return query;
            }
            Query("Inner").From(sources => sources.Set<PortableSourceOrder>().Join(sources.Set<PortableSourceCustomer>(),
                    order => order.CustomerId, customer => customer.Id,
                    (order, customer) => new PortableSourceView { OrderId = order.Id, Customer = customer.Name, Amount = order.Amount }))
                .OrderBy(row => row.OrderId).List();
            Query("Composite").From(sources => sources.Set<PortableSourceOrder>().Join(sources.Set<PortableSourceCustomer>(),
                    order => new { Id = order.CustomerId, order.Zone }, customer => new { customer.Id, customer.Zone },
                    (order, customer) => new PortableSourceView { OrderId = order.Id, Customer = customer.Name, Amount = order.Amount }))
                .OrderBy(row => row.OrderId).List();
            Query("Left").From(sources => sources.Set<PortableSourceOrder>()
                    .GroupJoin(sources.Set<PortableSourceCustomer>(), order => new { Id = order.CustomerId, order.Zone }, customer => new { customer.Id, customer.Zone },
                        (order, customers) => new { order, customers })
                    .SelectMany(pair => pair.customers.DefaultIfEmpty(),
                        (pair, customer) => new PortableSourceView { OrderId = pair.order.Id, Customer = customer == null ? null : customer.Name, Amount = pair.order.Amount }))
                .OrderBy(row => row.OrderId).List();
            Query("Matched").From(sources => sources.Set<PortableSourceOrder>()
                    .Where(order => sources.Set<PortableSourceCustomer>().Any(customer => customer.Id == order.CustomerId && customer.Zone == order.Zone))
                    .Select(order => order.Id)).OrderBy(id => id).List();
            Query("Unmatched").From(sources => sources.Set<PortableSourceOrder>()
                    .Where(order => !sources.Set<PortableSourceCustomer>().Any(customer => customer.Id == order.CustomerId && customer.Zone == order.Zone))
                    .Select(order => order.Id)).OrderBy(id => id).List();
            Query("Pairs").From(sources => sources.Set<PortableSourceOrder>().Where(order => order.Id == 1)
                    .SelectMany(order => sources.Set<PortableSourceCustomer>(), (order, customer) => customer.Name))
                .OrderBy(name => name).List();
            Query("Self").From(sources => sources.Set<PortableSourceOrder>().Join(sources.Set<PortableSourceOrder>(),
                    left => left.Id, right => right.Id, (left, right) => left.Id)).OrderBy(id => id).List();
            Query("Union").From(sources => sources.Set<PortableSourceOrder>().Select(row => row.Id)
                    .Union(sources.Set<PortableSourceCustomer>().Select(row => row.Id))).OrderBy(id => id).List();
            Query("Concat").From(sources => sources.Set<PortableSourceOrder>().Select(row => row.Id)
                    .Concat(sources.Set<PortableSourceCustomer>().Select(row => row.Id))).OrderBy(id => id).List();
            Query("Except").From(sources => sources.Set<PortableSourceOrder>().Select(row => row.Id)
                    .Except(sources.Set<PortableSourceCustomer>().Select(row => row.Id))).OrderBy(id => id).List();
            Query("Intersect").From(sources => sources.Set<PortableSourceOrder>().Select(row => row.Id)
                    .Intersect(sources.Set<PortableSourceCustomer>().Select(row => row.Id))).OrderBy(id => id).List();
            Query("Totals").Validate<PortableSourceRequest>(request => request.MinimumAmount >= 0, "Minimum must be non-negative.")
                .From<PortableSourceRequest, PortableSourceView>((sources, request) => sources.Set<PortableSourceOrder>()
                    .Where(order => order.Amount >= request.MinimumAmount)
                    .Join(sources.Set<PortableSourceCustomer>(), order => order.CustomerId, customer => customer.Id,
                        (order, customer) => new PortableSourceView { OrderId = order.Id, Customer = customer.Name, Amount = order.Amount }))
                .GroupBy(row => row.Customer ?? "", group => new PortableSourceTotal { Customer = group.Key, Count = group.Count(), Total = group.Sum(row => row.Amount) })
                .Where<PortableSourceRequest>((row, request) => row.Count >= request.MinimumCount).OrderBy(row => row.Customer).Paged();
            Query("Names").From(sources => sources.Set<PortableSourceOrder>().Join(sources.Set<PortableSourceCustomer>(),
                    order => order.CustomerId, customer => customer.Id, (order, customer) => new { order.Id, customer.Name }))
                .Dictionary(row => row.Id, row => row.Name);
            Query("Stream").From(sources => sources.Set<PortableSourceOrder>().Join(sources.Set<PortableSourceCustomer>(),
                    order => order.CustomerId, customer => customer.Id,
                    (order, customer) => new PortableSourceView { OrderId = order.Id, Customer = customer.Name, Amount = order.Amount }))
                .OrderBy(row => row.OrderId).Stream();
            Query("CustomersOnly").From(sources => sources.Set<PortableSourceCustomer>().Select(row => row.Name)).OrderBy(name => name).List();
            Query("BypassCustomers").BypassVisibility("Customers").From(sources => sources.Set<PortableSourceOrder>().Join(sources.Set<PortableSourceCustomer>(),
                    order => order.CustomerId, customer => customer.Id,
                    (order, customer) => new PortableSourceView { OrderId = order.Id, Customer = customer.Name, Amount = order.Amount }))
                .OrderBy(row => row.OrderId).List();
            Query("SourceGroups").From(sources => sources.Set<PortableSourceOrder>().GroupBy(row => row.CustomerId)
                    .Where(group => group.Count() > 1).OrderBy(group => group.Key)
                    .Select(group => new PortableSourceTotal { Customer = "group", Count = group.Count(), Total = group.Sum(row => row.Amount) })).List();
            Query("Conditional").From<PortableSourceRequest, int>((sources, request) => request.MinimumAmount == 0
                    ? sources.Set<PortableSourceOrder>().Select(row => row.Id)
                    : sources.Set<PortableSourceCustomer>().Select(row => row.Id)).OrderBy(id => id).List();
            Query("Count").From(sources => sources.Set<PortableSourceOrder>().Join(sources.Set<PortableSourceCustomer>(),
                order => order.CustomerId, customer => customer.Id, (order, customer) => order.Id)).Count();
            Query("Exists").From(sources => sources.Set<PortableSourceCustomer>().Select(row => row.Id)).Exists();
            Query("FirstValue").From(sources => sources.Set<PortableSourceCustomer>().Where(row => row.Id == 99).Select(row => row.Id)).FirstOrDefault();
            Query("Maximum").From(sources => sources.Set<PortableSourceOrder>().Select(row => row.Amount)).Max(amount => amount);
            Query("Slice").From(sources => sources.Set<PortableSourceOrder>().Join(sources.Set<PortableSourceCustomer>(),
                    order => order.CustomerId, customer => customer.Id,
                    (order, customer) => new PortableSourceView { OrderId = order.Id, Customer = customer.Name, Amount = order.Amount }))
                .OrderBy(row => row.OrderId).Slice();
            Query("Cursor").From(sources => sources.Set<PortableSourceOrder>().Join(sources.Set<PortableSourceCustomer>(),
                    order => order.CustomerId, customer => customer.Id,
                    (order, customer) => new PortableSourceView { OrderId = order.Id, Customer = customer.Name, Amount = order.Amount }))
                .OrderBy(row => row.OrderId).CursorPaged<int>((row, cursor) => row.OrderId > cursor, row => row.OrderId);
        });
    }
}
