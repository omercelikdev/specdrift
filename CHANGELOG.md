# Changelog

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
