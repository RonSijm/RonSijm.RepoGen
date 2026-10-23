namespace RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicResults;

internal sealed class Enrollment
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public Course? Course { get; set; }
}
