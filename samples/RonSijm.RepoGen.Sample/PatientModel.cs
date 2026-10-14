using Microsoft.EntityFrameworkCore;

namespace RonSijm.RepoGen.Sample;

[GenerateSelectors]
public sealed class Patient
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Email { get; set; } = null!;

    public DateOnly DateOfBirth { get; set; }

    public Guid PracticeId { get; set; }
}

[ProjectionFrom<Patient>]
public sealed record PatientSummaryDto(Guid Id, string Name);

[ProjectionFrom<Patient>]
public sealed record PatientReportDto(Guid Id, string Name, DateOnly DateOfBirth);

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Patient> Patients => Set<Patient>();
}

public sealed class PatientRepository(AppDbContext context)
{
    public PatientSelector ByEmail(string email) =>
        new(context, context.Patients.Where(patient => patient.Email == email));

    public PatientCollectionSelector ByPracticeId(Guid practiceId) =>
        new(context, context.Patients.Where(patient => patient.PracticeId == practiceId));
}