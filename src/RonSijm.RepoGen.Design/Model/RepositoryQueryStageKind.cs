namespace RonSijm.RepoGen.Design;

public enum RepositoryQueryStageKind
{
    Where,
    Select,
    OrderBy,
    OrderByDescending,
    ThenBy,
    ThenByDescending,
    Distinct,
    Skip,
    Take,
    GroupBy,
    Membership,
    OrderByField
}
