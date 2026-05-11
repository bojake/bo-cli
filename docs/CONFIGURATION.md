# Configuration Guide

BO gets more useful when it knows the vocabulary of your codebase.

Out of the box, `bo index` can infer a lot from file paths, package manifests, symbols, and
imports. But every serious system has local meaning: a folder called `adapters` might mean
infrastructure in one repo and UI glue in another. A good BO configuration names those
boundaries explicitly so the graph reflects the architecture people actually work in.

## Recommended Workflow

Use an AI assistant to draft the first configuration.

Suggested prompt:

```text
Inspect this repository and propose a BO configuration file.

Goal:
- identify architectural boundary names
- map folders and file patterns to those boundaries
- identify generated code, tests, migrations, scripts, and external integration layers
- suggest package or namespace classification rules
- keep the config conservative; do not invent boundaries that are not visible in the repo

Output:
- a proposed .bo/config.json
- a short explanation of each boundary
- any uncertain mappings that a human should review
```

Then review the result as a team and commit it with the codebase.

The config should be treated like `tsconfig.json`, `.editorconfig`, or test settings: part of
the repo's shared engineering contract.

## File Location

Recommended repo-local path:

```text
.bo/config.json
```

Future versions may support layered defaults, but the public repo-local file should be enough
for the initial CLI.

## Example

```json
{
  "schema_version": "0.1.0",
  "boundaries": [
    {
      "name": "ui",
      "description": "User interface components, pages, and client-side interaction code.",
      "path_patterns": [
        "src/components/**",
        "src/pages/**",
        "app/**/*.tsx"
      ]
    },
    {
      "name": "api",
      "description": "HTTP endpoints, route handlers, controllers, and request/response mapping.",
      "path_patterns": [
        "src/api/**",
        "src/controllers/**",
        "app/api/**"
      ]
    },
    {
      "name": "domain",
      "description": "Business rules, domain services, entities, policies, and use-case logic.",
      "path_patterns": [
        "src/domain/**",
        "src/services/**",
        "src/core/**"
      ]
    },
    {
      "name": "persistence",
      "description": "Database access, repositories, migrations, schema mapping, and storage adapters.",
      "path_patterns": [
        "src/db/**",
        "src/repositories/**",
        "migrations/**"
      ]
    },
    {
      "name": "integration",
      "description": "Calls to external services, queues, vendors, identity providers, and APIs.",
      "path_patterns": [
        "src/integrations/**",
        "src/clients/**",
        "src/adapters/**"
      ]
    },
    {
      "name": "jobs",
      "description": "Background work, scheduled tasks, queues, and workers.",
      "path_patterns": [
        "src/jobs/**",
        "src/workers/**"
      ]
    },
    {
      "name": "tests",
      "description": "Unit, integration, fixture, and acceptance tests.",
      "path_patterns": [
        "test/**",
        "tests/**",
        "**/*.test.*",
        "**/*.spec.*"
      ]
    },
    {
      "name": "generated",
      "description": "Generated code that should be indexed conservatively and ignored for refactor pressure.",
      "path_patterns": [
        "**/*.generated.*",
        "src/generated/**",
        "generated/**"
      ],
      "generated": true
    }
  ],
  "package_classification": {
    "internal_patterns": [
      "@your-org/**"
    ],
    "external_patterns": [
      "*"
    ]
  },
  "indexing": {
    "exclude_path_patterns": [
      "bin/**",
      "obj/**",
      "node_modules/**",
      "dist/**",
      "build/**",
      ".git/**",
      ".bo/**"
    ],
    "treat_generated_as_low_signal": true
  },
  "refactor_pressure": {
    "ignore_boundaries": [
      "tests",
      "generated"
    ]
  }
}
```

## Boundary Naming Advice

Use names that people already say in code review.

Good boundary names are:

- short: `api`, `domain`, `persistence`, `ui`
- stable across refactors
- visible in paths, namespaces, packages, or ownership
- meaningful to both humans and agents

Avoid names that are:

- too vague: `misc`, `common`, `stuff`
- too temporary: `new-api`, `v2-rewrite`
- too personal: `alice-area`, `bob-service`
- too aspirational: boundaries you wish existed but cannot yet map to code

## What AI Should Suggest

An AI assistant is especially useful for the first draft because it can scan patterns across
the whole repo quickly. Ask it to propose:

- boundary names
- path globs
- namespace or package naming patterns
- generated-code patterns
- test and fixture patterns
- obvious external integration folders
- uncertain areas for human review

The assistant should not decide the final architecture. It should create a reviewable proposal.

## What Humans Should Review

Before committing `.bo/config.json`, check:

- Do the boundary names match how the team talks?
- Are any important folders missing?
- Are tests and generated code classified correctly?
- Are external integrations separated from domain logic?
- Are package patterns too broad?
- Would a new engineer understand the map?

## Why This Matters

BO uses boundary names when it derives:

- boundary crossing records
- responsibility spread
- effect profiles
- refactor pressure
- pivot recommendations
- graph neighborhoods for coding agents

Bad boundaries produce noisy recommendations. Good boundaries make the graph feel like the
codebase as your team understands it.

