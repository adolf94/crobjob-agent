# Implementation Plan: homelab-cron-agent (Centralized Timer / External Scheduler)

Source spec: `~/thoughts/centralized_timer.md`
Created: 2026-10-10

## Overview

A single-container service hosted in the homelab that owns **all crons estate-wide**
(the 5 Azure Function `[TimerTrigger]` jobs → 1 local scheduler), eliminating timer
listeners and the storage-transaction bill they cause (~$27–30/yr estate-wide).

It provides:

- A **frontend management page** (CRUD): cron name, target API URL to trigger, cron
  schedule, timezone, enabled toggle, run-now, run history.
- A **scheduler engine**: evaluates cron schedules, POSTs the target endpoint
  (`?code=<function-key>`-style URLs), retries on non-2xx, records run history, and
  supports catch-up after downtime via optional `?date=` parameters.
- **SQLite** file-backed persistence (jobs + run history), volume-mounted in Docker.
- **Auth**: Atlas Rig OIDC (Authorization Code + PKCE) via `ar-auth-client` (React) on
  the frontend, JWT verification via `Ar.Auth.OpenId.AspNetCore` (`AddArAuth()`) on the
  API. All OIDC values are placeholders until a client is registered in the Auth UI.

## Architecture Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Runtime | .NET 10 (LTS) minimal API, single process | `Ar.Auth.OpenId.AspNetCore` SDK exists for one-line JWT verification; user's estate is .NET |
| Scheduler | Custom `BackgroundService` + **Cronos** library | Jobs are dynamic (created/edited at runtime via UI) — Quartz/Hangfire job stores are overkill; Cronos is tiny, DST-aware, supports 6-field cron |
| Database | SQLite (`Microsoft.Data.Sqlite`), WAL mode, one file on a Docker volume | Spec requirement ("litesql file"); WAL keeps scheduler writes off the UI read path |
| Frontend | React + Vite SPA, built into the same image, served as static files by ASP.NET | One container; `ar-auth-client` `AuthProvider`/`useAuth` does the PKCE flow |
| Auth | OIDC code flow + PKCE; API verifies `Authorization: Bearer` against `https://auth.adolfrey.com/.well-known/jwks.json`, issuer `https://auth.adolfrey.com/api` | Per Atlas Rig integration guide; no client_secret needed (public SPA client) |
| Target auth | Per job: `auth_mode` = `none` (anonymous URL, e.g. function-key-in-URL) or `arauth` (Atlas Rig app-principal `client_credentials` flow → `POST /api/token`, `scope=api://{target-client}/{scope}`), bearer token attached to dispatch | Matches spec decision 1 and Atlas Rig cross-app trust; secrets stored in SQLite, masked in API responses/UI |
| Failure visibility | Run history table + optional alert webhook (e.g. ntfy/Discord) on final dispatch failure | Spec requirement 4 + TBD "alerting" |
| Idempotency | Contract only (target endpoints must be idempotent, accept optional `?date=`); scheduler provides catch-up window per job | Spec requirements 2–3 |

### Config (placeholders until client is registered)

```
# appsettings.json / environment
ArAuth__Authority=https://auth.adolfrey.com/api          # for JWT verification
# Frontend (Vite env)
VITE_AR_AUTH_AUTHORITY=https://auth.adolfrey.com
VITE_AR_AUTH_CLIENT_ID=PLACEHOLDER_CLIENT_ID
VITE_AR_AUTH_REDIRECT_URI=https://PLACEHOLDER_HOST/callback
VITE_AR_AUTH_SCOPE=openid profile email PLACEHOLDER_SCOPE
# Backend authorization policy: require PLACEHOLDER_SCOPE present in the
# access token's scope claim (mapped to role claim by Atlas Rig).
ArAuth__RequiredScopes=PLACEHOLDER_SCOPE
```

### OIDC client registration (placeholder values, fill in Atlas Rig admin)

- Client ID: `PLACEHOLDER_CLIENT_ID`
- Required scope: `PLACEHOLDER_SCOPE` (e.g. `api://cron-agent/admin` — granted to
  allowed users in Atlas Rig admin; API rejects tokens without it)
- Redirect URIs to whitelist (register all that apply):
  - `https://PLACEHOLDER_HOST/callback` (domain)
  - `http://PLACEHOLDER_LAN_HOST:PORT/callback` (LAN, optional)

### SQLite schema (v1)

```sql
CREATE TABLE jobs (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    target_url TEXT NOT NULL,          -- includes ?code=<function key>
    cron_expression TEXT NOT NULL,     -- 6-field, Cronos-compatible
    timezone TEXT NOT NULL,            -- IANA, e.g. 'Asia/Kuala_Lumpur'
    enabled INTEGER NOT NULL DEFAULT 1,
    catchup_days INTEGER NOT NULL DEFAULT 1,
    max_retries INTEGER NOT NULL DEFAULT 3,
    append_date_param INTEGER NOT NULL DEFAULT 0,  -- adds &date=YYYY-MM-DD
    auth_mode TEXT NOT NULL DEFAULT 'none',        -- none | arauth
    auth_scope TEXT,                               -- e.g. api://ar-loans-app/run:job
    auth_client_id TEXT,                           -- app principal credentials
    auth_client_secret TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE runs (
    id INTEGER PRIMARY KEY,
    job_id INTEGER NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
    scheduled_for TEXT NOT NULL,       -- the tick this run belongs to
    started_at TEXT NOT NULL,
    finished_at TEXT,
    attempt INTEGER NOT NULL,
    status TEXT NOT NULL,              -- Running | Succeeded | Failed | Skipped
    http_status INTEGER,
    error TEXT
);

CREATE INDEX idx_runs_job ON runs(job_id, scheduled_for DESC);
```

### Container layout

```
homelab-cron-agent/
├─ src/
│  ├─ Api/                    # ASP.NET 10 minimal API + scheduler BackgroundService
│  │  ├─ Program.cs
│  │  ├─ Auth/                # AddArAuth wiring
│  │  ├─ Database/            # SQLite init, migrations, repositories
│  │  ├─ Jobs/                # CRUD endpoints
│  │  ├─ Scheduler/           # Cronos tick loop, dispatcher, retries, catch-up
│  │  └─ Alerts/              # webhook on final failure
│  └─ Web/                    # React + Vite SPA
│     └─ src/pages/{JobsList,JobEdit,JobHistory,Callback}.tsx
├─ Dockerfile                 # multi-stage: node build → dotnet build → runtime
├─ docker-compose.yml         # volume: ./data:/data (cron.db), healthcheck
└─ tasks/
```

## Task List

### Phase 1: Foundation

- [ ] **Task 1: Scaffold solution, container, and placeholder auth config**
  `Api` (minimal API, /healthz returning app version + DB path) + `Web` (Vite React,
  placeholder home page) + multi-stage `Dockerfile` + `docker-compose.yml` (volume
  `./data:/data`, healthcheck) + placeholder `VITE_AR_AUTH_*` / `ArAuth__*` config.
  **Verify:** `docker compose up` serves SPA at `/` and `/healthz` returns 200.
  Files: ~5 (scaffold). Size: M.

- [ ] **Task 2: SQLite persistence layer**
  `Microsoft.Data.Sqlite` with WAL, auto-migrate schema above at startup, repository
  interfaces + implementations for jobs and runs.
  **Verify:** unit tests cover migrate-on-fresh-DB, job CRUD via repo.
  Size: S.

### Checkpoint: Foundation
- [ ] Container builds and runs; `/healthz` OK; DB file created on volume

### Phase 2: Core features

- [ ] **Task 3: Auth end-to-end (frontend login + API JWT verification)**
  Wire `AddArAuth()` (Authority from config) on the API; all `/api/*` require auth.
  Frontend: `AuthProvider` from `ar-auth-client` with placeholder `clientId`,
  `/callback` route, login/logout in header, API client attaches Bearer token.
  **Verify:** with a registered client, login redirects through auth.adolfrey.com and
  a protected endpoint returns the user's `sub` for a user granted `PLACEHOLDER_SCOPE`;
  users/requests without the required scope are rejected (403/401); with placeholders,
  app shows login and fails gracefully (no crash) until registered.
  Size: M.

- [ ] **Task 4: Jobs CRUD API**
  `GET/POST/PUT/DELETE /api/jobs` (+ `POST /api/jobs/{id}/run` for run-now). Validate
  cron expression with Cronos and timezone with IANA lookup at write time. Never
  return the raw function key in list responses (mask to `…ast4`).
  **Verify:** integration tests on all endpoints incl. invalid cron/tz rejection.
  Size: S.

- [ ] **Task 5: Scheduler engine**
  `BackgroundService`: every ~15s compute next-due ticks per enabled job (Cronos +
  job timezone), dispatch via `HttpClient` (POST, timeout, `max_retries` with
  exponential backoff), write `runs` rows, honor `catchup_days` by back-running
  missed ticks (with `&date=` when `append_date_param`), per-job lock so overlapping
  dispatches skip (record `Skipped`).
  **Verify:** unit tests with fake clock: due-tick computation, retry on 500,
  catch-up back-run, no duplicate dispatch of the same tick across restarts.
  Size: L → split if needed: 5a tick loop, 5b dispatcher+retries, 5c catch-up.
  Depends: 2, 4.

- [ ] **Task 6: Frontend management pages**
  Jobs list (name, schedule, next-run, last status), create/edit form (name, API
  URL, cron expression with human-readable preview, timezone dropdown, enabled,
  catch-up days, retries, append-date flag), run history drawer per job, Run-now
  button, enable/disable toggle. Handle 401 → re-login.
  **Verify:** manual E2E in browser against running container with real client ID.
  Depends: 3, 4 (and 5 for next-run/history display).

### Checkpoint: Core
- [ ] Create a job pointing at a test endpoint → it fires on schedule, history
      records the run, edit/disable works, restart resumes without duplicates

### Phase 3: Ops & cutover

- [ ] **Task 7: Alerting + backup**
  Alert webhook config (`Alerts__WebhookUrl` placeholder) fired when a job exhausts
  retries or no run for a job in > expected interval. Backup: docker-compose comment/
  sidecar note for the SQLite file (copy-on-write snapshot script or Litestream).
  **Verify:** forced-failing job triggers webhook; backup restore test documented.
  Size: S.

- [ ] **Task 8: Homelab deployment + cutover checklist**
  Deploy container (reverse-proxy TLS, e.g. existing homelab ingress), register the
  OIDC client in Atlas Rig admin and fill real `CLIENT_ID`. Cutover per spec:
  convert each `ar-loans-app` timer (`InterestTimer`, `InterestNotification`,
  `FinanceSyncDrain`) to `[HttpTrigger(Function, "post")]` function-key endpoints,
  add jobs here, remove `[TimerTrigger]` attributes, deploy, verify with
  `StorageBlobLogs` that timer GetBlob/RenewBlobLease → ~0, repeat per remaining app.
  **Verify:** 24h StorageBlobLogs query; all 5 estate timers scheduled here.
  Size: S (repo) + ops checklist.

### Checkpoint: Complete
- [ ] All acceptance criteria met; estate timers migrated; storage txn drop confirmed

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| OIDC client not yet registered → blocked E2E auth | Med | Build with placeholders; `isPrototype`/bypass mode available for UI dev (per Atlas Rig docs) |
| Duplicated job runs after container restart | High | Persist last-dispatched tick per job in DB; only dispatch ticks > stored value (idempotency at scheduler level) |
| Host clock/DST drift misfires jobs | Med | Cronos + explicit IANA timezone per job; log next-run computation |
| SQLite corruption on abrupt container kill | Med | WAL mode + volume-backed file + backup task (7) |
| Function keys stored in plaintext DB | Low (homelab) | Restrict file perms, mask in UI; optional DPAPI/encryption follow-up |
| Target endpoint cold start / timeouts | Low | Spec accepts 3–10s cold start; HttpClient timeout ~60s + retries |

## Deviation log (as-built vs plan)

- **Backend JWT verification**: built-in `Microsoft.AspNetCore.Authentication.JwtBearer`
  pointing at `Authority=https://auth.adolfrey.com/api` (discovery doc verified standard)
  instead of `Ar.Auth.OpenId.AspNetCore` — avoids the GitHub Packages PAT and role-mapping
  is done via a custom `CronAccessHandler` (runtime-readable `Auth:Disabled` +
  `Auth:RequiredScopes`, verified against the `scope` claim).
- **Frontend auth**: `ar-auth-client` is not published on npm → used `oidc-client-ts`
  directly (the atlas-rig guide's Method 1 fallback), same PKCE flow.
- **DB schema additions vs v1**: `last_dispatched_utc` on jobs (restart-safe tick
  tracking), `auth_mode/auth_scope/auth_client_id/auth_client_secret` (per-target auth),
  plus name-unique conflict → HTTP 409.
- All placeholders retained: `PLACEHOLDER_CLIENT_ID`, `PLACEHOLDER_HOST`,
  `PLACEHOLDER_SCOPE` (frontend `.env`, compose, `appsettings.json`).

## Open Questions

- OIDC client registration: fill placeholders (`PLACEHOLDER_CLIENT_ID`,
  `PLACEHOLDER_HOST`, optional LAN host) and register the redirect URIs listed above.
- Alert channel preference: ntfy, Discord webhook, or email?
- Does any estate job need `?date=` back-fill semantics (interest accrual) vs plain
  re-run? (Affects `append_date_param` defaults per job.)
- Frontend theming/branding preferences (kept minimal for v1).
