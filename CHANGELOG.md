# Changelog

## [0.4.1] - 2026-07-10

### Fixed
- Schema: fractional `minimum`/`maximum` bounds were read as integers, so `minimum: 0.5`
  truncated to `0` and let `0.2` through in silence. Bounds on numbers are now compared as
  numbers. (`minLength`/`minItems` stay integer-valued, as the specification defines them.)
- Drift: a quoted `schemaVersion: "1"` — a string by the YAML reader's deliberate design —
  crashed `drift` with an unhandled `InvalidOperationException` instead of reporting
  `SPEC0221`. Manifest, profile and rules versions are now read defensively: a wrong type is
  a finding or a loud `FormatException`, never a cast. Messages render the JSON form, so `1`
  and `"1"` read apart.

## [0.4.0] - 2026-07-06

### Added
- Rules: the third constraint kind `deny` — "when the condition holds, this path must
  NOT equal that value" (e.g. outbox enabled → providers.broker must not be `none`).
- Drift: `in`-gated wiring — a feature counts as enabled for ANY listed value
  (`in: [openid, apikey]`), killing the false dead-weight report the single-value
  `equals` produced for multi-strategy providers.

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
