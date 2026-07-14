# specdrift

Deterministic spec lint for manifest-driven golden paths.

A golden path lives or dies by one promise: **the manifest is the single source of truth.**
Schemas validate the manifest's shape and static analyzers guard the code — but nothing
watches the space *between* artifacts, where "the manifest says X, the repo does Y" rots
silently. That gap is exactly where AI-generated changes decay. specdrift is the lint layer
for that gap, and it is deliberately boring: **it never calls an LLM — LLMs call it.**

```bash
dotnet tool install -g specdrift

specdrift validate .platform/manifest.yaml --schema manifest.schema.json --rules rules.yaml
```


## Run it anywhere

| How | One line |
|---|---|
| .NET tool | `dotnet tool install -g specdrift` |
| Docker (any stack) | `docker run --rm -v "$PWD:/work" -w /work ghcr.io/omercelikdev/specdrift:0.4.1 <args>` |
| GitHub Action | `- uses: omercelikdev/specdrift@v0.4.1` with `args:` |
| MCP server (agents) | `docker run --rm -i ghcr.io/omercelikdev/specdrift:0.4.1 mcp` — stdio tools `spec_validate`, `spec_drift` |

The image is multi-arch (amd64 + arm64). The engine is generic: the schema, rules and
drift profiles are DATA — point them at any repository, any language.

## What it does (v1)

- **`validate`** — JSON-Schema validation of a YAML/JSON manifest **plus cross-field
  invariants** you declare as data (`rules.yaml`): "when this path contains that value,
  this other path must exist." Every finding has a rule id, a severity, and a message that
  teaches the fix. Output: human text or `--format json`; exit code is the contract
  (0 clean · 1 findings · 2 usage/IO).

  Schema evaluation is an **in-tree, dependency-free evaluator** for the pragmatic 2020-12
  subset manifests actually use (`type · enum · const · required · properties ·
  additionalProperties · unevaluatedProperties · oneOf/anyOf/allOf/not · if/then/else ·
  $ref(#/…) · pattern · min/maxLength · min/maximum · items · uniqueItems · min/maxItems`).
  An unknown assertion keyword is a **hard fail** — the engine never guesses. Reports are
  noise-free by design: a failed `if` merely deselects its branch, and a failed `oneOf`
  yields ONE finding at the decision point, not one per alternative. (Why in-tree: every
  candidate library failed this project's own gates — one ships a maintenance-fee EULA in
  its NuGet binaries, one compiles code at runtime, one silently skips `if/then`.)
- **`drift`** — manifest ↔ repository reality, driven by a drift profile
  (`.specdrift/drift.yaml`): a feature enabled with its package or wiring call absent
  (each direction has its own finding — including "referenced but not enabled" dead
  weight), committed API documents diverging semantically from built ones, and manifest
  schema-version skew. Detection is TEXTUAL by design and says so; reports, never
  auto-fixes.

  ```yaml
  # .specdrift/drift.yaml
  version: 1
  manifest: .platform/manifest.yaml
  schemaVersion: 1
  wiring:
    - feature: features.outbox
      package: Platform.Messaging
      call: AddPlatformOutbox
    - feature: providers.auth       # value-gated wiring
      equals: openid
      package: Platform.Auth
      call: AddPlatformAuth
  openapi:
    - committed: specs/openapi.json
      built: artifacts/openapi.json
  ```
- **`mcp`** — the same two verbs served over stdio MCP (`spec_validate`, `spec_drift`), so
  coding agents ask the engine instead of guessing. Register it like any stdio server:

  ```json
  { "mcpServers": { "specdrift": { "command": "specdrift", "args": ["mcp"] } } }
  ```

## Profiles are data, the engine is generic

specdrift knows nothing about any particular platform. The schema, the invariant rules and
(coming with `drift`) the wiring tables are **profile data** that each golden path ships in
its own repository:

```yaml
# rules.yaml
version: 1
rules:
  - id: SPEC0101
    description: L2 caching needs a redis connection name
    when: { path: features.distributedCaching.levels, op: contains, value: l2 }
    require: { path: features.distributedCaching.redis.connectionName }
    severity: error
    message: "features.distributedCaching.levels includes 'l2' but redis.connectionName is missing - name the connection the cache should use."
```

Supported `when` ops: `exists`, `equals`, `contains` (array membership or substring).
Constraints: `require` (path must exist and be non-empty) and `forbid` (path must be absent).
A rule without `when` applies unconditionally.

## Determinism contract

- Same inputs → byte-identical report. No network, no clocks in output, no LLM.
- Unknown manifest schema versions are a **hard fail** — the engine never guesses forward.

## License

MIT.
