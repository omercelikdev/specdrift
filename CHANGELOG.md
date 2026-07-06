# Changelog

## [0.3.0] - 2026-07-06 (M3)

### Added
- `specdrift mcp`: a stdio MCP server exposing `spec_validate` and `spec_drift` — thin
  adapters over the same engines, returning the CLI's JSON findings. Proven at the
  protocol level: a raw JSON-RPC exchange (initialize → tools/list → tools/call) reads
  a real drift finding back from a fixture repo.

## [0.2.0] - 2026-07-06 (M2)

### Added
- `specdrift drift --repo <dir> [--profile <drift.yaml>]`: manifest ↔ repository reality.
  Wiring checks in both directions (feature on / package or call absent → error;
  referenced-but-not-enabled → dead-weight warning; value-gated features via `equals`),
  semantic committed-vs-built OpenAPI comparison (formatting is not drift), manifest
  schema-version skew, and a loud finding when the profile's manifest is missing.
  bin/obj/.git are never scanned; scans are ordered for byte-identical reports.
  Proof: a truthful generated app reports clean; flipping two manifest features with the
  code untouched names both exact gaps.

## [0.1.0] - 2026-07-06 (M1)

### Added
- `specdrift validate <manifest> --schema <schema.json> [--rules <rules.yaml>] [--format text|json]`:
  JSON-Schema validation of YAML/JSON manifests (YAML 1.2 core-schema scalar inference;
  quoted scalars stay strings on purpose) plus data-declared cross-field invariants
  (`when exists|equals|contains` → `require`/`forbid`, error/warning severities).
- Noise-free schema reporting: failed `if` branches and the failed alternatives of a
  passing `oneOf` are pruned — only the real cause surfaces.
- Determinism contract: byte-identical reports for identical inputs; unknown rules-file
  versions hard-fail (never guess forward). Exit codes: 0 clean · 1 error findings ·
  2 usage/IO.
- Gates from day one: license allowlist gate, Stryker mutation gate (break 70), CI on
  every push/PR.
