![Prayer](prayer.png)

# Prayer

Prayer is a platform backend for SpaceMolt clients.

## Story

On the outer rim, a pilot stared up at a distant star while strapping into a haphazard cargo container bolted to a vectored thruster.  
The seat was exposed to spray and vacuum grit, and every shudder from the engine sounded like a promise about to break.  
He smacked the side of the container, glanced at the void, and muttered, "Well, it's just you, me, and a prayer."  

Instead of coupling gameplay automation into each UI, Prayer sits in the middle and exposes one runtime/control API:

`Client (web/cli/agent)` -> `Prayer` -> `SpaceMolt`

Natural language should turn into SpaceMolt gameplay smoothly and execution should still be explicit, inspectable, and controllable.

## Core idea: natural language -> control language -> gameplay

Prayer’s killer feature is the control language plus the agent that writes it.

- A user expresses intent in natural language.
- The agent turns that intent into control-language script.
- Prayer executes script through deterministic runtime semantics.
- Clients observe state/status and can halt, resume, or edit at any point.

This keeps the UX conversational while avoiding black-box automation.

## Why it matters

Prayer is meant to be the shared control plane for many SpaceMolt clients, not just one app.

- Build new clients without rewriting SpaceMolt session/runtime plumbing.
- Keep planning/generation logic in one place.
- Keep execution semantics and safety controls in one place.
- Centralize telemetry and debugging.
- Keep client apps focused on interface and workflow.

If you are building dashboards, agent frontends, CLI tools, or custom automation clients, target Prayer and reuse the platform.

## What Prayer currently owns

- Runtime session lifecycle: `create`, `register`, `list`, `delete`
- Script lifecycle: `set`, `generate`, `execute`, `halt`, `save-example`
- Runtime state exposure: snapshot/status/state endpoints
- LLM catalog and preference persistence
- SpaceMolt transport, recovery handling, and instrumentation

DTO/API contracts are defined in `src/Prayer.Contracts`.

## Run and connect

Start Prayer:

```bash
dotnet run --project src/Prayer/Prayer.csproj
```

Start local stack (Prayer + app):

```bash
./scripts/dev-up.sh
```

Client config:

- `PRAYER_BASE_URL` (default `http://localhost:5000/`)

## API surface

Service and preferences:

- `GET /health`
- `GET /api/llm/catalog`
- `GET /api/preferences/bots`
- `PUT /api/preferences/bots`
- `GET /api/preferences/llm`
- `PUT /api/preferences/llm`

Runtime sessions:

- `GET /api/runtime/sessions`
- `POST /api/runtime/sessions`
- `POST /api/runtime/sessions/register`
- `GET /api/runtime/sessions/{id}`
- `DELETE /api/runtime/sessions/{id}`
- `GET /api/runtime/sessions/{id}/llm`
- `PUT /api/runtime/sessions/{id}/llm`

Runtime execution and state:

- `GET /api/runtime/sessions/{id}/snapshot`
- `GET /api/runtime/sessions/{id}/status`
- `GET /api/runtime/sessions/{id}/state`
- `POST /api/runtime/sessions/{id}/script`
- `POST /api/runtime/sessions/{id}/script/generate`
- `POST /api/runtime/sessions/{id}/script/execute`
- `POST /api/runtime/sessions/{id}/halt`
- `POST /api/runtime/sessions/{id}/save-example`
- `PUT /api/runtime/sessions/{id}/loop`
- `POST /api/runtime/sessions/{id}/commands`

Observability:

- `GET /api/runtime/sessions/{id}/spacemolt/stats`

`/state` also supports long polling via query params:

- `since=<version>`
- `wait_ms=<timeout>`

Recommended client pattern:

- One long-poll worker per session
- Separate command-dispatch path (don’t block commands on polling)

## Request examples

Create session:

```json
{
  "username": "your_bot_username",
  "password": "your_bot_password",
  "label": "optional-session-label"
}
```

Register and create session:

```json
{
  "username": "new_bot_username",
  "empire": "your_empire",
  "registrationCode": "registration_code",
  "label": "optional-session-label"
}
```

## Scope right now

Prayer currently targets trusted internal/single-operator deployments.

- No auth/authz layer yet
- No tenant isolation yet

That is intentional for the current phase: lock in the platform/runtime layer first, then harden for broader deployment.
