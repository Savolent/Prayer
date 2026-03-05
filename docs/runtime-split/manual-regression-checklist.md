# Runtime Split Manual Regression Checklist

Use this checklist after each migration step in `MIDDLE_RUNTIME_SPLIT_PLAN.md`.

## Preconditions

- Use the same bot account and similar in-game location where possible.
- Start with a clean app launch.
- Keep `docs/runtime-split/smoke-scripts.md` open and run the scripts in order.

## 1) DSL parse/normalize checks

1. Load `smoke-01-halt-only`.
2. Confirm script is accepted and runtime enters script mode.
3. Execute one tick and confirm runtime halts with a halt-related status.
4. Load `smoke-02-control-flow`.
5. Confirm parser accepts `repeat`, `if`, `until`, and `halt` without syntax errors.
6. Confirm invalid boolean token still fails (negative check with `if NOT_A_FLAG { halt; }`).

Expected:
- Valid scripts load without parse errors.
- Invalid boolean token produces a parse/format error.

## 2) Checkpoint save/restore checks

1. Load `smoke-03-multiturn-go` and allow at least one step to execute.
2. Stop app process while script state is non-empty.
3. Restart app and login same bot.
4. Confirm startup reports checkpoint restore success.
5. Confirm runtime resumes from prior script context (not reset to empty script).

Expected:
- Checkpoint file is read and restore succeeds.
- If checkpoint is intentionally corrupted, app should fail restore gracefully and start halted.

## 3) Multi-turn continuation checks (`go`, `mine`)

1. Run `smoke-03-multiturn-go`.
2. Observe that `go` may take multiple ticks/actions before completion.
3. Confirm runtime continues active command instead of selecting a new script step mid-route.
4. Run `smoke-04-multiturn-mine`.
5. Confirm `mine` continues across ticks until completion/stop condition.

Expected:
- Active multi-turn command remains active until it reports finished.
- After completion, runtime proceeds to next script step.

## 4) Docked enrichments checks

1. Dock at a station and refresh state.
2. Confirm docked panels/fields populate:
   - storage credits/items
   - market/deals
   - own buy/sell orders
   - shipyard lines/listings
   - available missions
3. Undock and refresh state.
4. Confirm docked-only fields reset to empty/default.

Expected:
- Docked enrichments present only when docked at station.
- Undocked snapshot does not retain stale docked-only values.

## 5) Retry and resilience spot-check

1. During scripted run, force one transient failure (e.g. a command failing due to temporary state).
2. Confirm runtime retries failing script step up to configured limit and then skips.

Expected:
- Retry message includes attempt counter.
- Runtime loop continues after retries exhausted.
