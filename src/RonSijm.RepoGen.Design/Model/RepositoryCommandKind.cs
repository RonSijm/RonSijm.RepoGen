namespace RonSijm.RepoGen.Design;

public enum RepositoryCommandKind
{
    Unspecified,
    Delete,
    Update,
    Create,
    Patch,
    TrackedDelete,
    InsertIfAbsent,
    InsertOrUpdate
}
