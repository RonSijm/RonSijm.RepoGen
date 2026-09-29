using RonSijm.RepoGen.Sample;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite(connection)
    .Options;
await using var context = new AppDbContext(options);
await context.Database.EnsureCreatedAsync();

var practiceId = Guid.NewGuid();
context.Patients.Add(new Patient
{
    Id = Guid.NewGuid(),
    Name = "Ron",
    Email = "ron@example.com",
    DateOfBirth = new DateOnly(1985, 4, 3),
    PracticeId = practiceId
});
await context.SaveChangesAsync();

var repository = new PatientRepository(context);
var report = await repository
    .ByEmail("ron@example.com")
    .AsReportDtoAsync();
var summaries = await repository
    .ByPracticeId(practiceId)
    .AsSummaryDtosAsync();

Console.WriteLine($"{report?.Name}: {summaries.Count} patient(s)");