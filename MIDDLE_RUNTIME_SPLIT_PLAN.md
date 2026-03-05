# Middle Runtime Extraction Plan

Goal: split MoltLang DSL, execution runtime, command executor, and state-building into a reusable middle runtime layer that can later sit between a client and SpaceMolt.

This plan is grounded in the current codebase seams:
- DSL: `src/Core/DSL/*`
- Executor/control flow: `src/Core/Agent/CommandExecutionEngine.cs`
- Runtime loop: `src/Core/BotRuntime.cs`
- State assembly: `src/Infra/SpaceMolt/SpaceMoltGameStateAssembler.cs`
- Current transport dependency: `src/Infra/SpaceMolt/SpaceMoltHTTPClient.cs`
- Command contracts using concrete transport: `src/Core/Commands/CommandContracts.cs`

## Working assumptions
- Start in-process (library boundary first), not a network service yet.
- Use incremental migration, preserving behavior at each step.
- Keep app/UI running during migration.

## How to use this document with a bot
- Run one step at a time in order.
- For each step: ask bot to implement only that step and run checks.
- Do not combine multiple steps unless all acceptance checks pass for current step.

---

## Step 0: Baseline and safety harness

### Objective
Create a stable baseline and simple regression harness before moving architecture.

### Actions
1. Create `docs/runtime-split/` and add:
   - `baseline-observations.md`
   - `manual-regression-checklist.md`
2. Record current behavior for:
   - Script parse/normalize (`repeat`, `if`, `until`, `halt`)
   - Checkpoint save/restore behavior
   - Multi-turn commands (`go`, `mine`) continue behavior
   - Docked enrichments (storage/market/shipyard/missions)
3. Add a lightweight smoke command script set under `docs/runtime-split/smoke-scripts.md`.

### Rationale
Architecture extraction without a baseline causes silent behavior drift. This step gives objective guardrails.

### Acceptance criteria
- Baseline docs exist and are committed.
- Team can run same manual checks after each migration step.

---

## Step 1: Introduce middle-runtime contracts (no behavior changes)

### Objective
Define runtime interfaces so core logic no longer depends on concrete `SpaceMoltHttpClient`.

### Actions
1. Add `src/MiddleRuntime/Contracts/` with interfaces:
   - `IRuntimeTransport`
   - `IRuntimeStateProvider`
   - `IRuntimeEngine`
2. Include operations currently required by commands/executor:
   - command execution
   - route lookup
   - map/catalog access
   - ship catalog pagination helpers
   - latest state retrieval
3. Add runtime-owned result models:
   - `RuntimeCommandResult`
   - `RuntimeExecutionResult`
4. Do not change existing command/executor wiring yet.

### Suggested interface shape
- Keep signatures async where current calls are async.
- Avoid leaking `JsonElement` outside transport boundary where practical.

### Rationale
Interfaces create a seam for extraction and future service mode while minimizing immediate churn.

### Acceptance criteria
- New contracts compile.
- No runtime behavior changes yet.

---

## Step 2: Add SpaceMolt adapter implementing new contracts

### Objective
Bridge current infra to new runtime contracts without changing behavior.

### Actions
1. Add `src/Infra/SpaceMolt/SpaceMoltRuntimeTransportAdapter.cs` implementing `IRuntimeTransport`.
2. Wrap existing `SpaceMoltHttpClient` calls one-for-one.
3. Keep session recovery/rate-limit behavior inside existing infra client.

### Rationale
Adapter pattern lets you migrate core logic first without destabilizing transport/session code.

### Acceptance criteria
- Adapter compiles and can be instantiated in `Program`.
- Existing runtime can still run with direct client (no cutover yet).

---

## Step 3: Decouple command contracts from concrete client

### Objective
Make command interfaces depend on runtime contract, not `SpaceMoltHttpClient`.

### Actions
1. Update `src/Core/Commands/CommandContracts.cs`:
   - Replace `SpaceMoltHttpClient` parameters with `IRuntimeTransport`.
2. Update all command implementations in `src/Core/Commands/*` to use new interface.
3. Keep behavior identical (no command logic changes beyond type replacement).

### Rationale
Commands are currently the heaviest coupling point to SpaceMolt infra.

### Acceptance criteria
- No command class references `SpaceMoltHttpClient` directly.
- Command behavior unchanged in smoke scripts.

---

## Step 4: Decouple execution engine from concrete client

### Objective
Make `CommandExecutionEngine` transport-agnostic.

### Actions
1. Update `src/Core/Agent/CommandExecutionEngine.cs`:
   - `ExecuteAsync` uses `IRuntimeTransport`.
2. Keep AST walker/checkpoint/memory semantics unchanged.
3. Ensure requeue/retry behavior in `BotRuntime` remains unchanged.

### Rationale
This isolates the core interpreter/executor loop from game transport details.

### Acceptance criteria
- `CommandExecutionEngine` compiles with no concrete transport dependency.
- Checkpoint restore and script stepping behavior unchanged.

---

## Step 5: Move DSL package boundary into middle runtime

### Objective
Relocate DSL ownership from `Core` to `MiddleRuntime`.

### Actions
1. Move or mirror `src/Core/DSL/*` into `src/MiddleRuntime/DSL/*`.
2. Update namespaces/imports in:
   - `CommandExecutionEngine`
   - `ScriptGenerationService`
   - any parser/normalizer callers
3. Keep grammar and normalization output behavior exactly the same.

### Rationale
DSL is part of runtime orchestration semantics, not app-specific glue.

### Acceptance criteria
- No DSL references remain under `src/Core/DSL` (or file becomes delegating shim only).
- Normalized output for baseline scripts matches pre-move output.

---

## Step 6: Extract runtime-state builder from SpaceMolt assembler

### Objective
Split state building into transport-side data retrieval and runtime-side projection/assembly.

### Actions
1. Add `src/MiddleRuntime/State/RuntimeStateBuilder.cs` with pure assembly logic.
2. Refactor `src/Infra/SpaceMolt/SpaceMoltGameStateAssembler.cs` to:
   - gather raw payloads and cache snapshots
   - call `RuntimeStateBuilder` for final state projection
3. Keep data-fetching concerns in Infra:
   - API calls
   - session handling
   - cache persistence

### Rationale
You want state-building in the middle layer so future clients share one canonical runtime state model.

### Acceptance criteria
- State structure observed in UI snapshots remains equivalent.
- Docked enrichments and mission/state sections still populate.

---

## Step 7: Introduce `IRuntimeStateProvider` and wire runtime to it

### Objective
Make runtime loop consume state via runtime contract instead of directly from `SpaceMoltHttpClient`.

### Actions
1. Add `SpaceMoltRuntimeStateProvider` in Infra implementing `IRuntimeStateProvider`.
2. Refactor `BotRuntime` (or successor) to use state provider for:
   - current snapshot reads
   - post-command refresh reads
3. Preserve auto-maintenance flow (complete mission, withdraw/refuel logic).

### Rationale
This removes the remaining direct state dependency path from core runtime loop.

### Acceptance criteria
- Runtime loop no longer calls `SpaceMoltHttpClient.GetGameState()` directly.
- Same halt/resume/loop semantics.

---

## Step 8: Create middle-runtime host façade

### Objective
Expose one middle-runtime API surface for app/UI integration.

### Actions
1. Add `src/MiddleRuntime/Host/RuntimeHost.cs` exposing methods such as:
   - `SetScript(...)`
   - `GenerateScript(...)`
   - `TickAsync(...)`
   - `Interrupt(...)`
   - `Halt(...)`
   - `GetSnapshot()`
2. Move orchestration responsibilities from `BotRuntime` into `RuntimeHost`.
3. Keep channel/UI-specific logic in App layer.

### Rationale
A host façade becomes the future service boundary with minimal additional refactor.

### Acceptance criteria
- `Program`/session wiring talks to `RuntimeHost` instead of raw executor+client composition.
- UI behavior remains unchanged.

---

## Step 9: Update app composition root to new layer boundaries

### Objective
Make `Program` wire App -> MiddleRuntime -> Infra explicitly.

### Actions
1. Refactor `src/App/Program.cs` composition to instantiate:
   - SpaceMolt infra client
   - runtime transport/state adapters
   - middle runtime host
2. Keep bot tabs/session UX unchanged.
3. Ensure checkpoint store still persists/restores execution state.

### Rationale
Composition root should reflect architecture, making future extraction to service straightforward.

### Acceptance criteria
- App starts and runs with identical external behavior.
- No direct App -> DSL internals coupling.

---

## Step 10: Hardening and cleanup

### Objective
Remove temporary compatibility shims and lock architecture boundaries.

### Actions
1. Delete deprecated direct dependencies and dead wrappers.
2. Add boundary notes in `docs/runtime-split/boundaries.md`:
   - App layer responsibilities
   - MiddleRuntime responsibilities
   - Infra responsibilities
3. Add CI/build checks if possible to prevent reintroducing forbidden references.

### Rationale
Without cleanup, couplings regress quickly.

### Acceptance criteria
- Clean build with no known temporary shims.
- Documented architectural ownership and dependency direction.

---

## Suggested dependency direction (target state)
- `App` -> `MiddleRuntime` -> `Infra.SpaceMolt`
- `MiddleRuntime` must not depend on concrete `SpaceMoltHttpClient`.
- `Infra.SpaceMolt` must not own DSL semantics or runtime control-flow rules.

---

## Risks and mitigations

1. Risk: behavior drift during DSL/engine moves.
- Mitigation: run baseline smoke scripts every step.

2. Risk: multi-turn commands regress (`go`, `mine`).
- Mitigation: keep command logic unchanged while only swapping interfaces.

3. Risk: state fields missing after state-builder split.
- Mitigation: compare UI snapshot sections before/after each step.

4. Risk: checkpoint incompatibility.
- Mitigation: preserve checkpoint schema until all migrations complete.

---

## Bot prompt templates (copy/paste one-by-one)

### Template A: execute one step
"Implement **Step X** from `MIDDLE_RUNTIME_SPLIT_PLAN.md` only. Keep behavior unchanged. Run build and report changed files + acceptance criteria results."

### Template B: verify step only
"Do not edit code. Verify whether **Step X acceptance criteria** in `MIDDLE_RUNTIME_SPLIT_PLAN.md` are met. Provide pass/fail per criterion with evidence."

### Template C: rollback scope control
"You edited beyond **Step X** scope. Revert out-of-scope changes and keep only files needed for Step X acceptance criteria."

---

## Definition of done for entire migration
- Middle runtime owns:
  - MoltLang DSL
  - executor/control-flow
  - runtime loop host
  - state-building rules
- Infra owns:
  - SpaceMolt API transport/session/recovery/caches
- App/UI depends on middle-runtime façade, not direct DSL/executor internals.
- Existing user-visible behavior remains equivalent.
