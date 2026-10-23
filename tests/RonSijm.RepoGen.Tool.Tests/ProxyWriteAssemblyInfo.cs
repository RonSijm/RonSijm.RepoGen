using System.Runtime.CompilerServices;
using Xunit;

[assembly: InternalsVisibleTo("RepoGenProxyWriteProduction")]

namespace RonSijm.RepoGen.Tool.Tests;

[CollectionDefinition("Proxy writes")]
public sealed class ProxyWriteAssemblyInfo;
