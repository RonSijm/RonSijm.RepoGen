using System.Runtime.CompilerServices;

// Only the actual persistence implementation test may access the internal database models.
// Generated test doubles are compiled independently, with no need for this friendship.
[assembly: InternalsVisibleTo("RepoGenProxyReadProduction")]
