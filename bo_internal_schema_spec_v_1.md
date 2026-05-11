# BO Internal Schema Specification v1

## Purpose

This specification defines the internal knowledge schema for BO, a CLI-first engineering reasoning engine. The schema is designed to support:

- code intelligence
- refactor pivot detection
- cross-repo reuse and repurpose decisions
- safe context planning for AI-assisted work
- backend-neutral planning and execution handoff

The schema is designed around **derived facts**, not comments or manually authored metadata.

---

## Design principles

1. **Facts over vibes**: prefer AST, symbol tables, type systems, dependency graphs, tests, and change history over comments or filenames.
2. **Normalized cross-language model**: BO should represent codebases in a language-neutral internal graph.
3. **Confidence-aware derivation**: inferred entities and edges must carry provenance, confidence, and freshness.
4. **Deterministic first**: parsing, relationship extraction, complexity metrics, and safe-edit set construction should be deterministic where possible.
5. **Decision traceability**: every recommendation must be explainable from stored evidence.

---

## Common conventions

### Global ID conventions

All primary entities MUST have a stable string identifier.

Recommended patterns:

- `repo:<name-or-hash>`
- `file:<repo_id>:<normalized_path>`
- `module:<repo_id>:<qualified_module_name>`
- `symbol:<repo_id>:<qualified_symbol_name>:<span_hash>`
- `contract:<symbol_id>:<ordinal>`
- `edge:<from_id>:<relation_type>:<to_id>`
- `interaction:<symbol_id>:<boundary_type>:<target_name>:<ordinal>`
- `decision:<decision_kind>:<target_id>:<timestamp-or-hash>`

### Common metadata fields

All stored entities SHOULD include these fields unless clearly not applicable:

- `id: string`
- `source_kind: enum`
- `source_version: string`
- `computed_at: timestamp`
- `confidence: float` in `[0.0, 1.0]`
- `staleness_hint: enum`

### Common enums

#### `source_kind`
- `ast`
- `lsp`
- `type_checker`
- `build_system`
- `runtime_probe`
- `git`
- `test_parser`
- `inference`
- `manual_override`

#### `staleness_hint`
- `fresh`
- `needs_reindex`
- `missing_dependency_context`
- `partial`
- `unknown`

#### `language_kind`
- `python`
- `typescript`
- `javascript`
- `go`
- `rust`
- `java`
- `csharp`
- `cpp`
- `c`
- `ruby`
- `php`
- `kotlin`
- `swift`
- `other`

#### `symbol_kind`
- `function`
- `method`
- `class`
- `interface`
- `struct`
- `enum`
- `type_alias`
- `constant`
- `variable`
- `constructor`
- `endpoint_handler`
- `job_handler`
- `event_handler`
- `test_case`
- `module_initializer`
- `unknown`

#### `visibility_kind`
- `public`
- `protected`
- `private`
- `internal`
- `package`
- `unknown`

#### `module_kind`
- `package`
- `namespace`
- `folder_module`
- `service`
- `library`
- `application`
- `plugin`
- `unknown`

#### `boundary_type`
- `db`
- `http`
- `filesystem`
- `queue`
- `cache`
- `auth`
- `crypto`
- `email`
- `search`
- `metrics`
- `logging`
- `clock`
- `feature_flag`
- `config`
- `ui`
- `unknown`

#### `operation_type`
- `read`
- `write`
- `delete`
- `publish`
- `consume`
- `authenticate`
- `authorize`
- `encrypt`
- `decrypt`
- `serialize`
- `deserialize`
- `render`
- `schedule`
- `measure`
- `log`
- `cache_get`
- `cache_put`
- `unknown`

#### `relation_type`
- `defines`
- `contains`
- `imports`
- `calls`
- `instantiates`
- `implements`
- `extends`
- `uses_type`
- `reads_from`
- `writes_to`
- `publishes_to`
- `subscribes_to`
- `tests`
- `changed_with`
- `similar_structure_to`
- `similar_behavior_to`
- `shares_dependency_profile_with`
- `belongs_to_capability`
- `duplicates_aspect_of`
- `crosses_boundary`
- `unknown`

#### `workflow_role`
- `orchestration`
- `validation`
- `policy`
- `mapping`
- `persistence`
- `transport`
- `integration`
- `caching`
- `auditing`
- `security`
- `computation`
- `presentation`
- `unknown`

---

## Section A: Entity definitions

## 1. Repo

Top-level repository container.

```json
{
  "entity": "Repo",
  "fields": {
    "id": "string",
    "name": "string",
    "root_path": "string",
    "default_branch": "string",
    "languages": "language_kind[]",
    "build_systems": "string[]",
    "package_managers": "string[]",
    "entrypoints": "string[]",
    "remote_urls": "string[]",
    "index_version": "string",
    "last_indexed_at": "timestamp",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Discover from CLI repo registry.
- Detect languages from file inventory and build manifests.
- Detect build systems/package managers from known manifest files.
- Detect entrypoints using framework-specific conventions plus call graph roots.

---

## 2. File

Physical source file.

```json
{
  "entity": "File",
  "fields": {
    "id": "string",
    "repo_id": "string",
    "path": "string",
    "normalized_path": "string",
    "language": "language_kind",
    "hash": "string",
    "size_bytes": "integer",
    "loc": "integer",
    "is_test": "boolean",
    "is_generated": "boolean",
    "module_id": "string|null",
    "parse_status": "string",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Discover from filesystem scan.
- Determine language from extension plus shebang/manifest context.
- Determine `is_test` from naming, path, and framework conventions.
- Determine `is_generated` from common generated-file markers and build outputs.

---

## 3. Module

Logical grouping above files and symbols.

```json
{
  "entity": "Module",
  "fields": {
    "id": "string",
    "repo_id": "string",
    "name": "string",
    "qualified_name": "string",
    "kind": "module_kind",
    "file_ids": "string[]",
    "public_symbol_ids": "string[]",
    "dependency_ids": "string[]",
    "boundary_types": "boundary_type[]",
    "fan_in": "integer",
    "fan_out": "integer",
    "cohesion_score": "float",
    "instability_score": "float",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Group by package/namespace/folder conventions.
- Merge with language-specific semantic boundaries when available from LSP.
- Compute fan-in/fan-out from symbol and module dependency edges.
- Compute cohesion using intra-module call density and dependency overlap.

---

## 4. Symbol

Primary reasoning primitive.

```json
{
  "entity": "Symbol",
  "fields": {
    "id": "string",
    "repo_id": "string",
    "file_id": "string",
    "module_id": "string|null",
    "qualified_name": "string",
    "display_name": "string",
    "kind": "symbol_kind",
    "language": "language_kind",
    "visibility": "visibility_kind",
    "signature": "string",
    "span_start": "integer",
    "span_end": "integer",
    "is_exported": "boolean",
    "is_entrypoint": "boolean",
    "is_test_subject": "boolean",
    "doc_hash": "string|null",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Parse using Tree-sitter plus language-aware LSP symbol providers where available.
- Normalize symbol kinds across languages.
- Determine visibility from AST and semantic info.
- Determine entrypoint status from framework conventions, exported roots, route bindings, job registries, and call graph roots.

---

## 5. Contract

Normalized input/output contract for a symbol.

```json
{
  "entity": "Contract",
  "fields": {
    "id": "string",
    "symbol_id": "string",
    "input_types": "string[]",
    "output_types": "string[]",
    "generic_constraints": "string[]",
    "throws_or_error_modes": "string[]",
    "schema_shapes": "object[]",
    "nullability": "object",
    "async_mode": "string",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Prefer type checker/LSP output over string parsing.
- Infer error modes from throws/returns, Result-like types, and tests.
- Infer schema shapes from DTOs, tagged structs, validation schemas, and serialization boundaries.

---

## 6. DependencyEdge

Normalized relationship between entities.

```json
{
  "entity": "DependencyEdge",
  "fields": {
    "id": "string",
    "from_id": "string",
    "to_id": "string",
    "from_kind": "string",
    "to_kind": "string",
    "relation_type": "relation_type",
    "strength": "float",
    "is_runtime": "boolean",
    "is_compile_time": "boolean",
    "is_test_only": "boolean",
    "evidence": "string[]",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- `imports`, `uses_type`: AST + LSP/type graph.
- `calls`, `instantiates`: call resolution via LSP/compiler APIs where available; AST fallback with lower confidence.
- `reads_from`, `writes_to`, `publishes_to`, `subscribes_to`: derive from boundary interaction extraction.
- `tests`: connect test artifacts to target symbols.
- `changed_with`: derive from change coupling windows.

---

## 7. BoundaryInteraction

First-class representation of an external or systemic interaction.

```json
{
  "entity": "BoundaryInteraction",
  "fields": {
    "id": "string",
    "symbol_id": "string",
    "boundary_type": "boundary_type",
    "operation_type": "operation_type",
    "target_name": "string",
    "effect_mode": "string",
    "framework_hint": "string|null",
    "evidence_spans": "object[]",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Match known framework adapters and client libraries.
- Use import graph + call targets + naming of invoked APIs.
- Use config and dependency manifests to improve classification.
- Extract evidence spans for explainability.

---

## 8. EffectProfile

Normalized summary of symbol side effects and systemic behavior.

```json
{
  "entity": "EffectProfile",
  "fields": {
    "id": "string",
    "symbol_id": "string",
    "reads_state": "boolean",
    "writes_state": "boolean",
    "emits_events": "boolean",
    "calls_external_service": "boolean",
    "mutates_input": "boolean",
    "has_retry_logic": "boolean",
    "has_transaction_logic": "boolean",
    "has_auth_logic": "boolean",
    "has_validation_logic": "boolean",
    "has_caching_logic": "boolean",
    "has_logging_logic": "boolean",
    "side_effect_classes": "string[]",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Summarize boundary interactions, AST features, decorators/annotations, and known framework semantics.
- Mark booleans conservatively; unknown should not be overclaimed.

---

## 9. ComplexityProfile

Local complexity metrics for a symbol or module.

```json
{
  "entity": "ComplexityProfile",
  "fields": {
    "id": "string",
    "target_id": "string",
    "target_kind": "string",
    "loc": "integer",
    "cognitive_complexity": "integer",
    "cyclomatic_complexity": "integer",
    "nesting_depth": "integer",
    "parameter_count": "integer",
    "branch_count": "integer",
    "halstead_volume": "float|null",
    "side_effect_count": "integer",
    "fan_in": "integer",
    "fan_out": "integer",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Compute from AST and dependency graph.
- `side_effect_count` should count distinct boundary/effect classes, not raw statement count.
- Module-level metrics should be aggregates plus distribution summaries in future versions.

---

## 10. ResponsibilityProfile

Design-centric summary of how many distinct concerns a target mixes.

```json
{
  "entity": "ResponsibilityProfile",
  "fields": {
    "id": "string",
    "target_id": "string",
    "boundary_type_count": "integer",
    "dependency_category_count": "integer",
    "capability_cluster_count": "integer",
    "side_effect_class_count": "integer",
    "responsibility_spread_score": "float",
    "dominant_responsibilities": "workflow_role[]",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Cluster dependencies and effects into categories.
- Infer workflow roles from effect profile, call patterns, and boundary interactions.
- Responsibility spread score SHOULD overweight mixed boundary types and mixed workflow roles.

---

## 11. ContextBurdenProfile

AI-oriented safe-edit context profile.

```json
{
  "entity": "ContextBurdenProfile",
  "fields": {
    "id": "string",
    "target_id": "string",
    "safe_edit_file_count": "integer",
    "safe_edit_symbol_count": "integer",
    "safe_edit_token_cost": "integer",
    "compression_ratio_required": "float",
    "ambiguity_score": "float",
    "subsystem_count_involved": "integer",
    "context_burden_score": "float",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Build a minimal safe-edit set from callers, callees, touched contracts, tests, and impacted boundary interactions.
- Estimate token cost from normalized file/symbol representations.
- `ambiguity_score` should reflect unresolved dynamic dispatch, overloaded names, and weak call resolution.

---

## 12. RefactorSeamCandidate

Detected modularization seam.

```json
{
  "entity": "RefactorSeamCandidate",
  "fields": {
    "id": "string",
    "target_id": "string",
    "seam_type": "string",
    "span": "object",
    "responsibility_label": "workflow_role",
    "dependency_reduction_estimate": "float",
    "context_reduction_estimate": "float",
    "breakage_risk": "float",
    "confidence": "float",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Detect contiguous spans or role-specific call clusters.
- Generate candidates where boundary interactions and workflow roles naturally separate.
- Estimate reduction by removing cross-boundary mixing and shrinking safe-edit set.

---

## 13. ChangeEvent

Normalized change artifact.

```json
{
  "entity": "ChangeEvent",
  "fields": {
    "id": "string",
    "repo_id": "string",
    "commit_hash": "string",
    "author": "string",
    "timestamp": "timestamp",
    "file_ids": "string[]",
    "symbol_ids": "string[]",
    "insertions": "integer",
    "deletions": "integer",
    "message_hash": "string",
    "is_revert": "boolean",
    "is_bugfix_proxy": "boolean",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Parse git history and map hunks to files and symbols.
- `is_bugfix_proxy` may be inferred from message patterns and test adjacency, but confidence must be reduced accordingly.

---

## 14. ChangeCouplingProfile

Co-change relationships over a time window.

```json
{
  "entity": "ChangeCouplingProfile",
  "fields": {
    "id": "string",
    "target_id": "string",
    "coupled_target_ids": "string[]",
    "coupling_frequency": "integer",
    "coupling_strength": "float",
    "window_days": "integer",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Compute over rolling windows from change events.
- Separate symbol-level and file-level coupling where possible.
- Strength should discount extremely large commits and repo-wide formatting changes.

---

## 15. StabilityProfile

Risk and volatility summary for an entity.

```json
{
  "entity": "StabilityProfile",
  "fields": {
    "id": "string",
    "target_id": "string",
    "churn_rate": "float",
    "author_count": "integer",
    "rework_rate": "float",
    "revert_rate": "float",
    "bugfix_rate_proxy": "float",
    "stability_score": "float",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Derived from change events and coupling data.
- Stability score should improve with low churn, low rework, low revert rate, and healthy test coverage where known.

---

## 16. TestArtifact

Behavioral evidence from tests.

```json
{
  "entity": "TestArtifact",
  "fields": {
    "id": "string",
    "repo_id": "string",
    "file_id": "string",
    "symbol_id": "string|null",
    "test_kind": "string",
    "target_symbol_ids": "string[]",
    "fixture_shapes": "object[]",
    "assertion_patterns": "string[]",
    "boundary_mocks": "string[]",
    "invariant_signals": "string[]",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Parse test frameworks by language.
- Map tests to target symbols using imports, invocation traces, naming, and fixture references.
- Extract invariants from assertions and expected error conditions.

---

## 17. BehavioralFingerprint

Inferred runtime-behavior summary.

```json
{
  "entity": "BehavioralFingerprint",
  "fields": {
    "id": "string",
    "target_id": "string",
    "input_shape_classes": "string[]",
    "output_shape_classes": "string[]",
    "state_transition_pattern": "string|null",
    "error_handling_pattern": "string|null",
    "retry_pattern": "string|null",
    "idempotency_signal": "float",
    "transactionality_signal": "float",
    "ordering_constraints": "string[]",
    "invariant_classes": "string[]",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Derived from contracts, tests, effect profile, and boundary interactions.
- Prefer evidence-backed patterns over speculative labeling.

---

## 18. CapabilityFingerprint

Cross-repo capability descriptor for a symbol or module.

```json
{
  "entity": "CapabilityFingerprint",
  "fields": {
    "id": "string",
    "target_id": "string",
    "entities": "string[]",
    "boundary_types": "boundary_type[]",
    "effect_types": "string[]",
    "workflow_role": "workflow_role",
    "dependency_categories": "string[]",
    "caller_classes": "string[]",
    "runtime_characteristics": "string[]",
    "security_characteristics": "string[]",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Build from effect profile, boundary interactions, contracts, caller population, and tests.
- This is the primary reusable fingerprint for cross-repo retrieval.

---

## 19. Aspect

Reusable aspect smaller than a module.

```json
{
  "entity": "Aspect",
  "fields": {
    "id": "string",
    "source_target_id": "string",
    "aspect_kind": "string",
    "fingerprint": "object",
    "quality_score": "float",
    "coupling_penalty": "float",
    "reuse_mode": "string",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Extract from repeated boundary/effect/behavior motifs.
- Examples: retry envelope, validation pipeline, audit publishing, error taxonomy, DTO mapping.
- Quality score should incorporate tests, churn stability, and coupling.

---

## 20. RefactorDecision

Stored modularization recommendation.

```json
{
  "entity": "RefactorDecision",
  "fields": {
    "id": "string",
    "target_id": "string",
    "rps": "float",
    "drivers": "string[]",
    "hard_gates_triggered": "string[]",
    "recommended_pivot_type": "string",
    "candidate_seam_ids": "string[]",
    "estimated_rps_after": "float",
    "estimated_context_after": "float",
    "risk_level": "string",
    "created_at": "timestamp",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Produced by the Refactor Pivot Engine from complexity, responsibility, architecture, change, and context profiles.
- `drivers` must reference concrete evidence categories.

---

## 21. ReuseCandidateDecision

Stored reuse or repurpose recommendation.

```json
{
  "entity": "ReuseCandidateDecision",
  "fields": {
    "id": "string",
    "request_target_id": "string",
    "candidate_target_id": "string",
    "rss": "float",
    "rcs": "float",
    "cis": "float",
    "recommendation": "string",
    "reusable_aspects": "string[]",
    "excluded_aspects": "string[]",
    "estimated_effort": "string",
    "estimated_risk": "string",
    "created_at": "timestamp",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Produced by ranking reuse candidates with RSS, RCS, and CIS.
- Must exclude low-integrity candidates from direct reuse.

---

## 22. PlanArtifact

Backend-neutral task work plan.

```json
{
  "entity": "PlanArtifact",
  "fields": {
    "id": "string",
    "task_text": "string",
    "target_ids": "string[]",
    "required_context_ids": "string[]",
    "token_budget": "integer",
    "predicted_steps": "string[]",
    "reuse_candidate_decisions": "string[]",
    "refactor_decisions": "string[]",
    "validation_plan": "string[]",
    "patch_strategy": "string",
    "created_at": "timestamp",
    "source_kind": "source_kind",
    "source_version": "string",
    "computed_at": "timestamp",
    "confidence": "float",
    "staleness_hint": "staleness_hint"
  }
}
```

### Derivation rules
- Produced by the planning engine.
- Must reference concrete target, context, and validation IDs rather than freeform prose alone.

---

## Section B: Derived score schemas

## 23. Refactor Pressure Score (RPS)

```json
{
  "entity": "RefactorPressureScore",
  "fields": {
    "id": "string",
    "target_id": "string",
    "local_complexity_score": "float",
    "responsibility_spread_score": "float",
    "architectural_stress_score": "float",
    "change_pain_score": "float",
    "context_burden_score": "float",
    "rps": "float",
    "trigger_class": "string",
    "computed_at": "timestamp"
  }
}
```

### Formula

```text
RPS =
  0.18 * CognitiveComplexityNorm +
  0.07 * NestingDepthNorm +
  0.05 * LOCNorm +
  0.20 * ResponsibilitySpreadNorm +
  0.15 * ArchitecturalStressNorm +
  0.15 * ChangePainNorm +
  0.20 * ContextBurdenNorm
```

### Trigger classes
- `none` for `< 35`
- `observe` for `35..49`
- `suggest_refactor` for `50..64`
- `strong_pivot` for `65..79`
- `refactor_now` for `>= 80`

### Hard pivot gates
Trigger a pivot even if aggregate RPS is lower when any of these fire:
- responsibility overload
- context overload
- high churn hotspot
- dependency hub instability
- repeated over-threshold growth

---

## 24. Reuse Suitability Score (RSS)

```json
{
  "entity": "ReuseSuitabilityScore",
  "fields": {
    "id": "string",
    "request_target_id": "string",
    "candidate_target_id": "string",
    "capability_similarity": "float",
    "structural_similarity": "float",
    "behavioral_similarity": "float",
    "interface_compatibility": "float",
    "quality_signal": "float",
    "rss": "float",
    "computed_at": "timestamp"
  }
}
```

### Formula

```text
RSS =
  0.28 * CapabilitySimilarity +
  0.22 * StructuralSimilarity +
  0.20 * BehavioralSimilarity +
  0.15 * InterfaceCompatibility +
  0.15 * QualitySignal
```

---

## 25. Repurpose Cost Score (RCS)

```json
{
  "entity": "RepurposeCostScore",
  "fields": {
    "id": "string",
    "request_target_id": "string",
    "candidate_target_id": "string",
    "dependency_mismatch": "float",
    "data_model_mismatch": "float",
    "security_mismatch": "float",
    "runtime_mismatch": "float",
    "hidden_side_effect_risk": "float",
    "coupling_penalty": "float",
    "rcs": "float",
    "computed_at": "timestamp"
  }
}
```

### Formula

```text
RCS =
  0.20 * DependencyMismatch +
  0.20 * DataModelMismatch +
  0.15 * SecurityMismatch +
  0.10 * RuntimeMismatch +
  0.20 * HiddenSideEffectRisk +
  0.15 * CouplingPenalty
```

---

## 26. Candidate Integrity Score (CIS)

```json
{
  "entity": "CandidateIntegrityScore",
  "fields": {
    "id": "string",
    "candidate_target_id": "string",
    "test_strength": "float",
    "churn_stability": "float",
    "low_defect_proxy": "float",
    "dependency_hygiene": "float",
    "low_coupling": "float",
    "production_usage_confidence": "float",
    "policy_compliance": "float",
    "cis": "float",
    "computed_at": "timestamp"
  }
}
```

### Formula

```text
CIS =
  0.20 * TestStrength +
  0.15 * ChurnStability +
  0.15 * LowDefectProxy +
  0.15 * DependencyHygiene +
  0.15 * LowCoupling +
  0.10 * ProductionUsageConfidence +
  0.10 * PolicyCompliance
```

### Reuse decision rules
- `reuse_directly` if `RSS >= 80` and `RCS <= 25` and `CIS >= 60`
- `wrap_and_reuse` if `RSS >= 70` and `RCS <= 45` and `CIS >= 60`
- `fork_and_improve` if `RSS >= 65` and `RCS <= 65` and `CIS >= 60`
- `reuse_aspects_only` if `RSS >= 50` and `RCS > 65`
- otherwise `ignore`

---

## Section C: Relation definitions and constraints

### Required foreign-key style relations
- `File.repo_id -> Repo.id`
- `Module.repo_id -> Repo.id`
- `Symbol.file_id -> File.id`
- `Symbol.module_id -> Module.id | null`
- `Contract.symbol_id -> Symbol.id`
- `BoundaryInteraction.symbol_id -> Symbol.id`
- `EffectProfile.symbol_id -> Symbol.id`
- `TestArtifact.target_symbol_ids -> Symbol.id[]`
- `CapabilityFingerprint.target_id -> Symbol.id | Module.id`
- `Aspect.source_target_id -> Symbol.id | Module.id`
- `RefactorDecision.target_id -> Symbol.id | Module.id | File.id`
- `ReuseCandidateDecision.request_target_id -> Symbol.id | Module.id | PlanArtifact.id`
- `ReuseCandidateDecision.candidate_target_id -> Symbol.id | Module.id | Aspect.id`
- `PlanArtifact.target_ids -> Symbol.id | Module.id | File.id[]`

### Integrity constraints
1. A `Symbol` MUST belong to exactly one `File`.
2. A `BoundaryInteraction` MUST reference exactly one `Symbol`.
3. A `Contract` MUST reference exactly one `Symbol`.
4. A `RefactorDecision` MUST reference at least one evidence-bearing profile.
5. A `ReuseCandidateDecision` MUST reference corresponding RSS/RCS/CIS records.
6. A `PlanArtifact` MUST reference explicit context IDs rather than only freeform text.

---

## Section D: Derivation pipeline order

Recommended pipeline:

1. repo scan
2. file inventory
3. AST parse
4. symbol extraction
5. contract extraction
6. dependency edge extraction
7. boundary interaction extraction
8. effect profile derivation
9. complexity profile computation
10. responsibility profile derivation
11. test artifact extraction
12. behavioral fingerprint derivation
13. change event ingestion
14. change coupling computation
15. stability profile computation
16. capability fingerprint derivation
17. context burden computation
18. aspect extraction
19. RPS / RSS / RCS / CIS scoring
20. decision generation
21. plan generation

---

## Section E: Storage model recommendation

Initial relational tables:

- `repos`
- `files`
- `modules`
- `symbols`
- `contracts`
- `dependency_edges`
- `boundary_interactions`
- `effect_profiles`
- `complexity_profiles`
- `responsibility_profiles`
- `context_burden_profiles`
- `refactor_seam_candidates`
- `change_events`
- `change_coupling_profiles`
- `stability_profiles`
- `test_artifacts`
- `behavioral_fingerprints`
- `capability_fingerprints`
- `aspects`
- `refactor_pressure_scores`
- `reuse_suitability_scores`
- `repurpose_cost_scores`
- `candidate_integrity_scores`
- `refactor_decisions`
- `reuse_candidate_decisions`
- `plan_artifacts`

Recommended first backend:
- SQLite or DuckDB

---

## Section F: Implementation notes for Codex or other coding backends

1. Parse and derive deterministic entities first.
2. Avoid using LLMs for core extraction logic.
3. Persist evidence spans for all inferred decisions.
4. Keep cross-language normalization in one dedicated package.
5. Treat comments and docstrings as optional weak signals only.
6. Keep every scoring formula explicit and configurable.
7. Version the schema and derivation rules so indexes can be migrated.

---

## Section G: Minimum viable subset for BO v1

Required entities for first shipping milestone:

- Repo
n- File
- Module
- Symbol
- Contract
- DependencyEdge
- BoundaryInteraction
- EffectProfile
- ComplexityProfile
- ResponsibilityProfile
- ContextBurdenProfile
- ChangeEvent
- ChangeCouplingProfile
- TestArtifact
- CapabilityFingerprint
- RefactorPressureScore
- ReuseSuitabilityScore
- RepurposeCostScore
- CandidateIntegrityScore
- RefactorDecision
- ReuseCandidateDecision
- PlanArtifact

Note: fix typo in implementation to remove stray `n-` before `File`.

---

## Section H: Suggested next spec artifacts

After this schema, define:

1. `bo_schema.json` — machine-readable schema
2. `derivation_rules.md` — algorithmic rules and confidence strategy
3. `storage_schema.sql` — initial relational schema
4. `scoring_config.json` — tunable weights and thresholds
5. `cli_contract.md` — commands and JSON output contracts

These should be treated as source-of-truth implementation inputs for Codex.

