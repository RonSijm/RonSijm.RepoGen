namespace RonSijm.RepoGen.Testing;

internal interface IInMemoryStoreState
{
    Action PrepareCaptureBaseline();
    Action PrepareRestoreBaseline();
    Action PrepareRestoreCheckpoint();
    void ClearRows();
    (Type RowType, object Rows) SnapshotRows();
}
