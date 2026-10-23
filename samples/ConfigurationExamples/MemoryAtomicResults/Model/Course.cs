namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicResults;

internal sealed class Course
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
}
