namespace RonSijm.RepoGen.ConfigurationExamples.Contracts.MappedSetters;

public interface IEditableNoteReader
{
    Task<IReadOnlyList<EditableNoteSeed>> CardsAsync(CancellationToken cancellationToken = default);
}
