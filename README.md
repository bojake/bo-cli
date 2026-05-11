# bo-cli

**AI-native code intelligence for people and agents who need source-code facts they can query.**

`bo-cli` turns a source repository into a semantic graph: files, modules, symbols,
contracts, dependencies, side effects, complexity, responsibility spread, and refactor
pressure. It is designed to feed BogDB, MCP servers, coding agents, and human reviewers
with structured evidence instead of loose codebase impressions.

The project is Apache 2.0 and built in the open by Beyond Ordinary.

## Why It Exists

Modern coding agents are powerful, but they still lose time rediscovering code structure
from raw text. `bo-cli` gives them a deterministic map before they start planning:

- what symbols exist, where they live, and how they relate
- what files and methods carry unusual complexity or responsibility
- what dependencies and boundary crossings shape a change
- what refactor pivot points are worth investigating first
- what structured graph data can be handed to BogDB or an MCP codegen server

BO does not ask a model to parse code, count complexity, or infer dependency edges from
prose. Those jobs belong to repeatable analyzers. Models can then use the graph to plan,
explain, and generate code with better context.

## Configuration

BO works best when it knows the boundary names your team actually uses: UI, API, domain,
persistence, integrations, jobs, tests, generated code, and any project-specific slices that
matter in your architecture.

The recommended setup is to have an AI assistant inspect the repository and propose a
`.bo/config.json` boundary configuration, then have a human review and commit it. The
configuration is not a secret; it is a shared map of how the codebase should be understood.

See [Configuration Guide](docs/CONFIGURATION.md).

## Core Commands

```bash
bo init
bo index --json
bo index --json --full
bo pivot src/ExampleService.cs --json
```

`bo index --json --full` is the primary integration path for downstream graph tooling. It
emits the structured records that BogDB's codegen MCP server can ingest.

## What It Produces

The public CLI is centered on a stable JSON contract:

- `Repo`, `Module`, and `File` records
- `Symbol` records for classes, interfaces, functions, methods, records, structs, and enums
- `Contract` records for signatures, inputs, outputs, async behavior, nullability, and errors
- file and symbol dependency edges
- boundary interaction records for database, network, filesystem, auth, logging, cache, and queue behaviors
- effect, complexity, and responsibility profiles
- refactor pressure scores and pivot recommendations

Those records are useful directly as JSON, and more useful when persisted into BogDB as a
queryable code-intelligence graph.

## BogDB Integration

BogDB is the embedded graph database. `bo-cli` is one producer of BogDB-ready code graphs.

The first public implementation should depend on the published BogDB NuGet package, not a
local sibling checkout. That gives both projects a clean contract: BO proves the package works
for real downstream tooling, and BogDB gets an immediate AI-native consumer.

The intended public flows are:

```bash
bo index --json --full > codegraph.json
```

or, once BogDB-backed persistence is wired into the CLI:

```bash
bo index --store bogdb --db .bo/codegraph.bogdb
```

Then a BogDB MCP/codegen tool can query the graph and expose semantic tools such as:

- find a symbol
- show callers and callees
- estimate change impact
- inspect a file's dependency neighborhood
- identify high-pressure refactor candidates

This split keeps the database product clean while letting `bo-cli` evolve as a focused
developer and agent tool.

## Design Principles

- Deterministic analysis first. LLMs should not be the parser.
- Structured output by default. If an agent needs it, it should be machine-readable.
- Local-first operation. The CLI should work on a checkout without a hosted service.
- Graph-native shape. The data model should map cleanly into BogDB nodes and relationships.
- Boring security posture. No credentials in output, no hidden network calls for local indexing.
- Clear extension seams. Enterprise search, hosted orchestration, and governed workflows can
  layer on top without bloating the open CLI.

## Project Status

`bo-cli` is being extracted from Beyond Ordinary's internal BO work into a clean public
Apache 2.0 repository.

The first public milestone is intentionally focused:

- package the CLI and core analyzers
- reference BogDB through its public NuGet package
- document repo-local boundary configuration
- preserve the JSON output contract used by BogDB MCP integration
- include representative fixtures and golden tests
- keep orchestration, hosted workflow, and customer-specific integrations out of the initial cut

## Planned Public Surface

```text
BO.Cli/
  Program.cs
  CliJsonWriter.cs

BO.Core/
  Indexing/
  Services/Bootstrap/
  Services/Index/
  Persistence/
    InMemory/
    Null/
    BogDb/
  Configuration/
  Ids/

BO.Tests/
  Fixtures/
  *ExtractorTests.cs
  *DeriverTests.cs
  IndexGoldenOutputTests.cs
  RefactorPressureScorerTests.cs
```

The initial repository should avoid private orchestration clients, live claim/lease workflows,
credentials, local absolute paths, and dependencies on unpublished sibling repositories. BogDB
integration should come through NuGet.

## License

Apache License 2.0. See [LICENSE](LICENSE).
