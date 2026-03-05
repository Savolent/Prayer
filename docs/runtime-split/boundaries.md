# Runtime Split Boundaries

## Dependency direction

- `App` -> `MiddleRuntime` -> `Infra.SpaceMolt`
- `MiddleRuntime` does not depend on concrete `SpaceMoltHttpClient`.
- `Infra.SpaceMolt` does not own DSL semantics or runtime control-flow rules.

## App responsibilities

- Own UI transport (HTMX/web handlers), bot tab selection, and bot lifecycle wiring.
- Send runtime requests as `{ bot_id, command, argument? }`.
- Persist user-facing/session metadata and route status/snapshots to UI.

## MiddleRuntime responsibilities

- Own runtime host orchestration (`RuntimeHost`) and execution loop behavior.
- Own DSL parsing/normalization/interpreter and command execution semantics.
- Own runtime state projection rules (`RuntimeStateBuilder`) and runtime contracts.

## Infra responsibilities

- Own SpaceMolt HTTP transport/session lifecycle/recovery/rate-limit behavior.
- Own API/cache/persistence integration and adapter implementations for runtime contracts.
- Provide data/state to middle runtime via contract adapters (`IRuntimeTransport`, `IRuntimeStateProvider`).
