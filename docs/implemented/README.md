# Implemented features

These guides describe available behavior, with configuration examples and deliberate boundaries.
Advanced features remain optional. Start with the simple preset and enable only the capabilities
your repository needs.

## Start here

1. [Generation options](generation/GENERATION-OPTIONS.md): simple/compatibility presets and switches.
2. [Code-first generation](generation/CODE-FIRST-GENERATION.md): independent runner and output ownership.
3. [Closed repositories](repositories/CLOSED-REPOSITORIES.md): EF-free named application contracts.
4. [Configuration examples](../../samples/ConfigurationExamples/README.md): designer-to-output walkthroughs.

## Topics

| Topic | Guides |
| --- | --- |
| [Generation and validation](generation/README.md) | Runners, compiler checks, multiple contexts, API snapshots and diagnostics |
| [Repository contracts and infrastructure](repositories/README.md) | Contracts, adapters, tracking, caching, context/transaction lifetimes and providers |
| [Queries and projections](queries/README.md) | Query composition, result terminals, loading and sorting |
| [Writes and saved results](writes/README.md) | Commands, batches, concurrency, collection/link writes and returned DTOs |
| [Test doubles and fixtures](testing/README.md) | Scripted doubles, bounded portable behavior and real-provider contract suites |

"Implemented" does not mean every entity shape or database semantic is simulated.
Use each guide's validation/coverage section for its exact support boundary.

[Documentation index](../README.md)
