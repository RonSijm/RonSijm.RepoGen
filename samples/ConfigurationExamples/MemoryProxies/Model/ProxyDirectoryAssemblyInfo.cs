using System.Runtime.CompilerServices;

// The shared examples build designer/persistence validation separately from the model.
// Only those infrastructure assemblies receive access; the EF-free consumer does not.
[assembly: InternalsVisibleTo("Designers")]
[assembly: InternalsVisibleTo("RepoGenValidation_Implementation")]
