# Documentation

For what RepoGen does and how to start, see the [main README](../README.md).
For supported APIs and their configuration, use the implemented-feature guides below.

## By status

| Section | What belongs here |
| --- | --- |
| [Implemented features](implemented/README.md) | Usage guides for shipped capabilities, including their opt-ins and supported-shape boundaries |
| [Work in progress](work-in-progress/README.md) | Explicitly active, unfinished implementation work; not every possible extension |
| [Future work](future/README.md) | Roadmap context and unimplemented candidates; not promises of available APIs |
| [Other](other/README.md) | Research inventories and historical delivery/verification records |

An implemented guide describes the supported subset, not universal SQL/provider equivalence.
Historical delivery counts and original recommendations are kept out of the current usage guide.

## Implemented features by topic

- [Generation and validation](implemented/generation/README.md): code-first runners, settings,
  output layouts, compiler checks, diagnostics and API baselines.
- [Repository contracts and infrastructure](implemented/repositories/README.md): closed/CQRS
  contracts, tracking, adapters, contexts, unit of work, caching and providers.
- [Queries and projections](implemented/queries/README.md): fragments, multiple sources,
  result pipelines, ordering, paging, dictionaries, loading and visibility.
- [Writes and saved results](implemented/writes/README.md): typed commands, batches,
  reconciliation, concurrency and saved/native results.
- [Test doubles and fixtures](implemented/testing/README.md): scripts, portable fixture
  behavior, explicit mappings and shared real-provider contract tests.

The [configuration examples](../samples/ConfigurationExamples/README.md) connect each designer
to its generated output and consumer.
