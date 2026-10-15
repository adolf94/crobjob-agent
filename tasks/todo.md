# Task List: homelab-cron-agent

Plan: `tasks/plan.md`

## Phase 1: Foundation
- [ ] Task 1: Scaffold solution, container, placeholder auth config
- [ ] Task 2: SQLite persistence layer (schema + repositories)

## Checkpoint: Foundation
- [ ] Container builds/runs, `/healthz` OK, DB file created on volume

## Phase 2: Core
- [ ] Task 3: Auth end-to-end (`ar-auth-client` SPA + `AddArAuth()` API)
- [ ] Task 4: Jobs CRUD API + run-now endpoint
- [ ] Task 5: Scheduler engine (Cronos tick loop, dispatcher, retries, catch-up)
- [ ] Task 6: Frontend management pages (jobs list/edit/history)

## Checkpoint: Core
- [ ] End-to-end: job fires on schedule, history recorded, restart-safe

## Phase 3: Ops & cutover
- [ ] Task 7: Alert webhook + SQLite backup strategy
- [ ] Task 8: Homelab deployment + Azure timer cutover checklist

## Checkpoint: Complete
- [ ] All acceptance criteria met; estate timers migrated; txn drop verified
