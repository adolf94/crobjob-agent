# homelab-cron-agent

Centralized cron scheduler for the homelab: owns the schedules that used to live as
Azure Function `[TimerTrigger]`s and fans them out over HTTP with retries, run
history, catch-up, and a management UI. See `tasks/plan.md` for the design.

- **Stack**: .NET 10 (LTS) minimal API + React/Vite SPA (one container), SQLite file DB, Cronos scheduler
- **Auth**: Atlas Rig OIDC — code flow + PKCE via `oidc-client-ts` (SPA); JWT verified against `https://auth.adolfrey.com/api`, plus optional required scope
- **Targets**: each job POSTs a configured URL — either **anonymous** (function key in URL) or **Atlas Rig** (`auth_mode=arauth`): the API itself is a registered confidential client and requests an access token via `client_credentials` using its own `Auth__ClientId`/`Auth__ClientSecret`, attaching the bearer to dispatches (token cached per scope; jobs only store their target scope)

## Run locally

```bash
dotnet run --project src/Api          # API+UI on launch profile
cd src/Web && npm run dev             # dev SPA with /api proxy to :5000
dotnet test tests/Api.Tests           # 41 tests
```

Disable auth for local dev: set `Auth:Disabled=true` (env `Auth__Disabled=true`).

> **Note:** auth is currently **disabled by default** everywhere (appsettings + compose)
> until the Atlas Rig client is registered. To enable: set `Auth:Disabled=false`,
> `Auth__RequiredScopes` to the granted scope, and rebuild the SPA with real
> `VITE_AR_AUTH_*` values.

## Deploy (container)

```bash
docker compose up -d --build
# with real auth values baked into the SPA:
VITE_AR_AUTH_CLIENT_ID=xxx docker compose up -d --build
```

Volume `./data:/app/data` holds `cron.db` (SQLite, WAL). Backup: copy
`data/cron.db` while the container is stopped, or run a periodic
`sqlite3 data/cron.db ".backup '/backup/cron.db'"` sidecar job — WAL mode allows
online backups.

## Configuration (placeholders until registered in Atlas Rig)

| Variable | Placeholder value |
|---|---|
| `VITE_AR_AUTH_CLIENT_ID` | `PLACEHOLDER_CLIENT_ID` |
| `VITE_AR_AUTH_SCOPE` | `openid profile email PLACEHOLDER_SCOPE` |
| `Auth__RequiredScopes` | `PLACEHOLDER_SCOPE` |
| `Auth__Authority` | `https://auth.adolfrey.com/api` |
| `Auth__ClientId` / `Auth__ClientSecret` | confidential client for outbound `client_credentials` dispatches |
| Redirect URI to whitelist | `https://<host>/callback` |

## API (JWT-protected)

| Method | Route | Purpose |
|---|---|---|
| GET | `/healthz` | public health probe |
| GET/POST | `/api/jobs` | list / create (URL function keys masked in responses) |
| GET/PUT/DELETE | `/api/jobs/{id}` | get / update (blank or masked URL = keep existing) |
| GET | `/api/jobs/{id}/runs` | recent run history |
| POST | `/api/jobs/{id}/run` | run now (same retry/alert path as scheduled runs) |
