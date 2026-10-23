using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RonSijm.RepoGen.Tool.Tests;

internal sealed class SavedBatchCommandCapture : DbCommandInterceptor
{
    internal List<string> Queries { get; } = [];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Queries.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
