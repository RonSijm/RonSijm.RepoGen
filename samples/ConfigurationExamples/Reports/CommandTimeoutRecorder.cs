using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RonSijm.RepoGen.ConfigurationExamples.Reports;

// Infrastructure verification only: application ports have no runtime timeout controls.
internal sealed class CommandTimeoutRecorder : DbCommandInterceptor
{
    public List<int> Timeouts { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Timeouts.Add(command.CommandTimeout);
        return ValueTask.FromResult(result);
    }
}