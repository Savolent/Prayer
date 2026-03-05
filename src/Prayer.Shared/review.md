# Architecture Review Status (validated 2026-03-05)

### High: runtime state contract is untyped (`JsonElement? State`)
**Status:** Partially mitigated.

Legacy `/api/runtime/sessions/{id}/state` still sends `JsonElement?`, and the app client still manually deserializes it.
New `/api/runtime/sessions/{id}/state-v2` now sends typed `RuntimeGameStateDto? State` for schema/codegen consumers.

**Current refs**
- `src/Prayer.Contracts/ApiContracts.cs:45`
- `src/Prayer.Contracts/ApiContracts.cs:54`
- `src/Prayer/Program.cs:619`
- `src/Prayer/Program.cs:664`
- `src/App/PrayerApiClient.cs:199`

### Medium: `GameState` is both DTO and rendering/domain logic
**Status:** Still true.

`GameState` includes render/display formatting methods in addition to transport fields. This couples payload shape with presentation and makes external client portability less clear.

**Current refs**
- `src/Prayer/Core/State/GameStateRendering.cs:5`
- `src/Prayer/Core/State/GameStateRendering.cs:111`
- `src/Prayer/Core/State/GameStateRendering.cs:229`

### Medium: flat/ambiguous top-level shape
**Status:** Still true.

Top-level state still mixes many parallel scalars (`Fuel/MaxFuel`, `Hull/MaxHull`, `Shield/MaxShield`, etc.) and naming like `System` vs `Systems`.

**Current refs**
- `src/Prayer.Shared/GameStateModels.cs:12`
- `src/Prayer.Shared/GameStateModels.cs:16`
- `src/Prayer.Shared/GameStateModels.cs:36`
- `src/Prayer.Shared/GameStateModels.cs:45`

### Medium: weakly typed semantic fields
**Status:** Still true.

Several fields that look like enums/timestamps/structured payloads remain plain strings/ints (`AcceptedAt`, `Type`, `Online`, `PayloadJson`).

**Current refs**
- `src/Prayer.Shared/GameStateModels.cs:231`
- `src/Prayer.Shared/GameStateModels.cs:239`
- `src/Prayer.Shared/GameStateModels.cs:273`
- `src/Prayer.Shared/GameStateModels.cs:292`
