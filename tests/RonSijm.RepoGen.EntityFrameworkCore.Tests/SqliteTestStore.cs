using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.EntityFrameworkCore.Tests;

internal sealed class SqliteTestStore : IAsyncDisposable
{
    private SqliteTestStore(
        SqliteConnection connection,
        PatientDbContext context,
        CommandCaptureInterceptor interceptor,
        Guid practiceId)
    {
        Connection = connection;
        Context = context;
        Interceptor = interceptor;
        PracticeId = practiceId;
        Repository = new PatientRepository(context);
    }

    public SqliteConnection Connection { get; }

    public PatientDbContext Context { get; }

    public CommandCaptureInterceptor Interceptor { get; }

    public Guid PracticeId { get; }

    public PatientRepository Repository { get; }

    public static async Task<SqliteTestStore> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new CommandCaptureInterceptor();
        var options = new DbContextOptionsBuilder<PatientDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        var context = new PatientDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var practiceId = Guid.NewGuid();
        context.Patients.AddRange(
            new Patient
            {
                Id = Guid.NewGuid(),
                Name = "Ron",
                Email = "ron@example.com",
                DateOfBirth = new DateOnly(1985, 4, 3),
                PracticeId = practiceId,
                Score = 7
            },
            new Patient
            {
                Id = Guid.NewGuid(),
                Name = "Ada",
                Email = "ada@example.com",
                DateOfBirth = new DateOnly(1815, 12, 10),
                PracticeId = practiceId,
                Score = 11
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        interceptor.Clear();

        return new SqliteTestStore(connection, context, interceptor, practiceId);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
    }
}