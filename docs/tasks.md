# AZ KOTLE — Task Backlog

Každý task: **Cíl**, **Akceptační kritéria** (checklist), **Deliverables**. Jeden task = jedna session. Nevedou se paralelně.

---

## FÁZE 0 — Bootstrap (týden 1)

### TASK 0.1 — Solution a repo skeleton

**Cíl:** .NET 10 solution s projekty dle B.3, prázdné, kompilovatelné.

**Akceptační kritéria:**
- [ ] `dotnet new sln -n AzKotle` + všechny projekty (classlib / web / webapi).
- [ ] Reference mezi projekty dle B.3.
- [ ] `dotnet build` bez warnings.
- [ ] `.gitignore` pro .NET + JetBrains + VS Code.
- [ ] `README.md` s popisem + quickstart.
- [ ] `CLAUDE.md` (ČÁST A).
- [ ] `docs/context.md` (ČÁST B).
- [ ] `.editorconfig` dle Microsoft .NET guidelines.
- [ ] `Directory.Packages.props` pro centrální správu NuGet verzí.

**Deliverables:** commit `chore: initial solution skeleton`.

### TASK 0.2 — Docker + docker-compose pro lokální vývoj

**Cíl:** Postgres + Redis + Seq v containeru.

**Akceptační kritéria:**
- [ ] `deploy/docker-compose.dev.yml`: Postgres 16, Redis 7, Seq.
- [ ] Named volumes pro persistence.
- [ ] `.env.example` s proměnnými (bez reálných hodnot).
- [ ] `Makefile` / `justfile`: `up`, `down`, `logs`, `psql`.
- [ ] `docker compose up -d` → vše healthy do 30 s.

### TASK 0.3 — CI pipeline (GitHub Actions)

**Cíl:** Každý PR: build + test + lint.

**Akceptační kritéria:**
- [ ] `.github/workflows/ci.yml`: triggers on PR + push main.
- [ ] Kroky: checkout → setup-dotnet@v4 (10.0.x) → restore → build → test (s Coverlet coverage) → upload coverage artifact.
- [ ] `dotnet format --verify-no-changes` jako součást CI.
- [ ] Cache NuGet packages.

### TASK 0.4 — Pre-commit hook a konvence

**Cíl:** Lokální quality gates.

**Akceptační kritéria:**
- [ ] Husky.Net nebo `.git/hooks/pre-commit`.
- [ ] `dotnet format`, `dotnet build`, rychlé testy (`--filter Category=Unit`).
- [ ] Conventional commits linter.

---

## FÁZE 1 — Multi-tenant foundation (týdny 2–3)

### TASK 1.1 — Domain entity: Tenant, User, TenantMembership

- [ ] `Domain/Entities/Tenant.cs`, `User.cs`.
- [ ] Strongly-typed IDs (`TenantId`, `UserId`, record struct).
- [ ] Domain events: `TenantCreated`, `UserInvited`, `UserActivated`.
- [ ] Invariants v private setters + factory (`Tenant.Create(slug, companyName, ico)`).
- [ ] Unit testy pro všechny invariants.

### TASK 1.2 — EF Core + PostgreSQL + Initial migration

- [ ] `Infrastructure/Persistence/AzKotleDbContext.cs`.
- [ ] Entity configurations v `Infrastructure/Persistence/Configurations/*.cs`.
- [ ] Initial migration vytvoří tabulky z B.4.
- [ ] Migration aplikuje RLS policies (raw SQL).
- [ ] `AddAzKotleDb()` extension pro DI.
- [ ] Integrační test: Testcontainers Postgres, RLS ověřen (user A nevidí data user B).

### TASK 1.3 — Tenant resolution middleware

- [x] `Api/MultiTenancy/TenantResolutionMiddleware.cs`.
- [x] Strategie: 1) JWT claim `tenant_id`, 2) subdoména.
- [x] `HttpContext.Items["AzKotle.TenantId"]` a `ITenantContext.Current` (přes `HttpTenantContext`).
- [x] ~~`ISaveChangesInterceptor` → `SET LOCAL app.current_tenant_id`~~ — revidováno v TASK 1.2, viz [ADR 001](adr/001-rls-connection-interceptor.md). Izolace běží přes `DbConnectionInterceptor` + session-level `set_config`, který pokrývá čtení i zápis.
- [ ] ~~Integrační test: cross-tenant access = 404~~ — přesunuto do TASK 2.2 (zatím není business endpoint, aby dávalo smysl). Middleware testy v 1.3 pokrývají JWT, subdoménu, reserved subdomain, unknown slug, precedence a `[AllowAnonymousTenant]`.

### TASK 1.4 — Auth endpoints (register, login, refresh)

- [x] `POST /api/v1/auth/register` — vytvoří tenant + owner usera (AllowAnonymousTenant).
- [x] `POST /api/v1/auth/login` → `{ accessToken, refreshToken, expiresIn }` (vyžaduje tenant subdoménu).
- [x] `POST /api/v1/auth/refresh` — rotace refresh tokenu + reuse-detection (revoke chain).
- [x] `POST /api/v1/auth/logout` — revoke refresh token (`RequireAuthorization`).
- [x] Argon2id hashing (`Konscious.Security.Cryptography.Argon2`; Iterations=4, Memory=64MB, Parallelism=4, SaltSize=16, HashSize=32; formát `$argon2id$v=19$m=...$salt$hash`).
- [x] JWT HS256, issuer/audience z konfigu, 15 min access / 30 dní refresh.
- [x] FluentValidation (české hlášky).
- [x] Integrační testy: happy path + 7 edge cases (dup email/slug 409, wrong password 401, revoked/reuse refresh, no/invalid bearer 401).

### TASK 1.5 — Blazor United shell + login page

- [x] Blazor United template (interactive server + WASM).
- [x] Layout: `MainLayout.razor` s `MudAppBar` (navbar). Sidebar odložen do fáze 2 (chybí menu položky).
- [x] Stránky: `/login`, `/register`, `/dashboard` (placeholder), `/logout`, `/` (landing).
- [x] `JwtAuthenticationStateProvider` napojený na JWT z localStorage (`BrowserStorage`/`AuthSession`).
- [x] **Design system: MudBlazor** (volba uživatele — žádný npm, rychlejší iterace, `MudDataGrid` pro TASK 2.4).
- [x] Brand barvy v `AzKotleTheme`: primary `#0F6B8A`, accent `#D97706`, text `#0F1A24`.
- [x] Responsive — `MudGrid`/`MudContainer MaxWidth="Small"`, form funguje na 360px.
- [x] Api: CORS policy + `TenantSlug` fallback v `LoginRequest`/`RefreshRequest` (dev bez subdomény).
- [x] **Bonus** — ARES integrace (`GET /api/v1/lookup/ares/{ico}` + tlačítko „Načíst z ARES" v Register formu; autofill `CompanyName`). Původně plánováno v fázi 2 (context.md B.7).
- [ ] Transparent token refresh na 401 — odloženo (TASK 1.6 nebo přidat do fáze 2).

---

## FÁZE 2 — Evidence kotlů + QR (týdny 4–5)

### TASK 2.1 — Domain: Customer, Location, Boiler

### TASK 2.2 — CRUD API: /api/v1/customers, /locations, /boilers
- [ ] GET (pagination + filter), GET by id, POST, PUT, DELETE (soft delete).
- [ ] Cursor-based pagination (`{ items, nextCursor }`).
- [ ] FluentValidation, české hlášky.
- [ ] Integrační testy + cross-tenant access test.
- [ ] OpenAPI (Swashbuckle) na `/swagger` (Development).

### TASK 2.3 — QR kód generování a discovery endpoint
- [ ] Formát `AK-XXXX-XX` (Crockford Base32).
- [ ] Unique constraint + retry at race.
- [ ] QRCoder PNG/SVG.
- [ ] `/boilers/{id}/qr-label` — A4 tisková šablona se 4 QR štítky.
- [ ] Public `/qr/{code}` — anonymní → redirect na login; přihlášený technik → detail kotle.

### TASK 2.4 — Blazor UI: seznam kotlů, detail, editace
- [ ] `/boilers` — data grid (virtual scroll, filter, search).
- [ ] `/boilers/new` — form s validací.
- [ ] `/boilers/{id}` — detail: info, poslední revize, fotky, historie.
- [ ] Optimistic UI.
- [ ] E2E test (Playwright): create → edit → delete.

---

## FÁZE 3 — Revizní zpráva a PDF (týdny 6–8)

### TASK 3.1 — Inspection form engine
- [ ] Šablona jako JSON schema (`docs/templates/nv191_2022.json`).
- [ ] Blazor `<DynamicForm Schema="..." Model="..." />`.
- [ ] Fields: text, číslo, select, boolean, date, textarea, photo upload, signature pad.
- [ ] Autosave (debounce 5s) do `inspections.form_data` status `draft`.

### TASK 3.2 — PDF generator (QuestPDF)
- [ ] `Infrastructure/Pdf/InspectionReportPdf.cs`.
- [ ] Hlavička (logo firmy, údaje, číslo zprávy), sekce (zákazník, lokalita, kotel, měření, závady, doporučení, podpisy).
- [ ] Font Noto Sans embedded (česká diakritika).
- [ ] PDF/A-2b compliance.
- [ ] Snapshot test vs `tests/snapshots/inspection_example.pdf`.

### TASK 3.3 — Backblaze B2 storage
- [ ] `Infrastructure/Storage/B2Storage.cs` implementuje `IFileStorage`.
- [ ] AWS SDK for .NET (S3) s custom endpoint.
- [ ] Stream upload, async, progress reporting.
- [ ] Presigned URL 15min TTL pro download.
- [ ] Key schema: `tenants/{tenantId}/inspections/{year}/{inspectionId}.pdf`.
- [ ] Polly retry 3× exponential.
- [ ] Integrační test proti MinIO (Testcontainers).

### TASK 3.4 — Inspection sign flow
- [ ] `POST /api/v1/inspections/{id}/sign`.
- [ ] Pipeline: validate → generate PDF → SHA256 → upload B2 → update DB → queue email.
- [ ] Background worker (Hangfire/Quartz).
- [ ] `audit_log` zápis (IP, UA).
- [ ] UI: „Podepsat a odeslat" → loading → success screen + download link.

---

## FÁZE 4 — Deploy a monitoring (týden 9)

### TASK 4.1 — Production Dockerfile
- [ ] Multi-stage (SDK 10 → ASP.NET 10 alpine).
- [ ] Final image < 250 MB.
- [ ] Non-root user.
- [ ] Healthcheck `/health` + `/health/ready`.
- [ ] `docker scan` bez HIGH/CRITICAL.

### TASK 4.2 — docker-compose.prod.yml + Caddyfile
- [ ] Služby: `app`, `api`, `postgres`, `redis`, `caddy`, `seq`.
- [ ] Caddy: auto-HTTPS pro `app.az-kotle.cz`, `api.az-kotle.cz`.
- [ ] HSTS + security headers.
- [ ] Postgres: daily backup cron → B2.
- [ ] Seq: admin API key, retention 30 dní.

### TASK 4.3 — GitHub Actions deploy
- [ ] Push do `main` → deploy.
- [ ] Build Docker images → push GHCR → SSH na VPS → `docker compose pull && up -d` → smoke test (`curl /health`).
- [ ] Secrets: `VPS_SSH_KEY`, `GHCR_TOKEN`.
- [ ] Rollback: tag previous image jako `:previous`.

### TASK 4.4 — Observability
- [ ] Serilog → Seq, structured logging, correlation ID.
- [ ] `prometheus-net` na `/metrics`.
- [ ] Sentry SDK.
- [ ] Grafana dashboard JSON v `deploy/grafana/dashboards/overview.json`.
- [ ] Uptime monitoring (UptimeRobot / BetterStack) pro `/health`.
