# Runtime Split Plan Tracker

This file is the working tracker for runtime extraction progress.
Detailed implementation plan remains in `MIDDLE_RUNTIME_SPLIT_PLAN.md`.

## Status (as of 2026-03-05)

- [x] Step 0: Baseline and safety harness
- [x] Step 1: Introduce middle-runtime contracts (no behavior changes)
- [ ] Step 2: Add SpaceMolt adapter implementing new contracts
- [ ] Step 3: Decouple command contracts from concrete client
- [ ] Step 4: Decouple execution engine from concrete client
- [ ] Step 5: Move DSL package boundary into middle runtime
- [ ] Step 6: Extract runtime-state builder from SpaceMolt assembler
- [ ] Step 7: Introduce `IRuntimeStateProvider` and wire runtime to it
- [ ] Step 8: Create middle-runtime host facade

## Step 0 completion evidence

- `docs/runtime-split/baseline-observations.md`
- `docs/runtime-split/manual-regression-checklist.md`
- `docs/runtime-split/smoke-scripts.md`

## Step 1 completion evidence

- `src/MiddleRuntime/Contracts/IRuntimeTransport.cs`
- `src/MiddleRuntime/Contracts/IRuntimeStateProvider.cs`
- `src/MiddleRuntime/Contracts/IRuntimeEngine.cs`
- `src/MiddleRuntime/Contracts/RuntimeCommandResult.cs`
- `src/MiddleRuntime/Contracts/RuntimeExecutionResult.cs`

## Notes

- Current migration stance keeps session/retry/rate-limit handling in `SpaceMoltHttpClient` during early extraction steps to minimize behavioral risk.
- Runtime-layer session ownership can be introduced later as an explicit contract once transport decoupling is complete.
