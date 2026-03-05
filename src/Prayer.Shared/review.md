High: duplicated DTO definitions are already drifting

GameState/related models exist in both Prayer core and shared, with behavior differences.
Example: core ItemStack has constructor + mutation methods, shared ItemStack is plain settable DTO.
This creates contract drift risk between server/client and weakens portability.
Refs: GameState.cs (line 9), GameState.cs (line 942), GameStateModels.cs (line 4), GameStateModels.cs (line 156)
High: runtime state contract is untyped (JsonElement? State)

Server serializes GameState into JSON element; client deserializes manually.
No compile-time contract for the most important payload; easier to break across refactors/languages.
Refs: ApiContracts.cs (line 45), Program.cs (line 619), PrayerApiClient.cs (line 199)
Medium: GameState is both DTO and rendering/domain logic

It includes formatting/render pipelines and computed behavior, not just transport data.
Makes “shared model” non-lean and harder to port (Go client would need to infer what is contract vs presentation).
Refs: GameState.cs (line 106), GameState.cs (line 189)
Medium: flat/ambiguous top-level shape

System vs Systems, many parallel scalar fields (Fuel/MaxFuel, Hull/MaxHull, etc.) instead of grouped objects.
Harder to version and map cleanly in other languages.
Refs: GameStateModels.cs (line 6), GameStateModels.cs (line 29), GameStateModels.cs (line 38)
Medium: weakly typed semantic fields

Date/time and enum-like fields are strings/ints (AcceptedAt, Type, Online, PayloadJson).
This is brittle for consumers and portable SDK generation.
Refs: GameStateModels.cs (line 167), GameStateModels.cs (line 175), GameStateModels.cs (line 201), GameStateModels.cs (line 220)