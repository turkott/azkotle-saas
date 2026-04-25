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

- [x] Strongly-typed IDs (`CustomerId`, `LocationId`, `BoilerId`).
- [x] `Customer` aggregate (Person/Company, IČO jen pro Company, kontaktní info, rename, notes) + `CustomerCreated` event.
- [x] `Location` aggregate (adresa + GPS VO, customer FK) + `LocationCreated` event.
- [x] `Boiler` aggregate (QR `AK-XXXX-XX` Crockford Base32, manufacturer/model/sn/output_kw/fuel/installed_at, RecordInspection invariants) + `BoilerRegistered`/`BoilerInspectionRecorded` events.
- [x] Unit testy pro všechny invariants (51 testů: 14 Customer + 12 Location + 13 Boiler).

### TASK 2.2 — CRUD API: /api/v1/customers, /locations, /boilers
- [x] GET (pagination + filter), GET by id, POST, PUT, DELETE.
- [x] Cursor-based pagination (`{ items, nextCursor }`) — cursor je timestamp-based (CreatedAt). Tie-breaking pro shodné CreatedAt řešíme v 2.4 pokud bude potřeba.
- [x] FluentValidation, české hlášky.
- [x] Integrační testy + cross-tenant access test (Testcontainers PG s non-superuser `azkotle_app` rolí pro skutečné RLS enforcement). 9 testů: happy CRUD pro každý zdroj, cross-tenant 404 pro Customer + Boiler, validace, pagination, anonymous → 401.
- [x] EF konfigurace + migrace `AddCustomersLocationsBoilers` s FORCE RLS policies.
- [x] QR slug generátor (placeholder Crockford Base32 v Infrastructure; QR image + PDF štítek v 2.3).
- [ ] ~~DELETE soft delete~~ — odloženo. Hard delete pro MVP; soft delete přidáme až bude potřeba audit trail.
- [ ] ~~OpenAPI (Swashbuckle) na `/swagger`~~ — odloženo. Minimal API má `MapOpenApi()` na `/openapi/v1.json`; Swagger UI doplníme později.

### TASK 2.3 — QR kód generování a discovery endpoint
- [x] Formát `AK-XXXX-XX` (Crockford Base32) — implementováno v TASK 2.2 (`BoilerQrSlugGenerator`).
- [x] Unique constraint + retry at race — `BoilerEndpoints.CreateAsync` retry max. 5× při Postgres `23505`.
- [x] QRCoder PNG (`IQrCodeImageRenderer` → `QrCoderImageRenderer`, ECC level Q).
- [x] `/api/v1/boilers/{id}/qr-label` — A4 PDF s mřížkou QR štítků (počet konfigurovatelný `?copies=`, default 4, max 24). QuestPDF Community License.
- [x] `/api/v1/boilers/{id}/qr.png` — bonus PNG endpoint pro samostatný download.
- [x] Public `/qr/{code}` — anonymní → 302 na `{Web:BaseUrl}/login?redirect=/qr/{code}`; přihlášený → 302 na detail kotle. RLS zajišťuje, že přihlášený uživatel uvidí jen QR ze svého tenantu.

### TASK 2.4 — Blazor UI: seznam kotlů, detail, editace
- [x] `/boilers` — `MudDataGrid` se search (QR/výrobce/SN), barevné chipy pro „další termín revize" (zelená > 30 dní, žlutá < 30, červená po termínu), klik na řádek → detail.
- [x] `/boilers/new` — form s `MudAutocomplete` pro zákazníka, dynamický `MudSelect` pro lokality podle vybraného zákazníka, validace.
- [x] `/boilers/{id}` — detail (info, poslední revize, další termín, QR PNG inline), edit specs, „Zaznamenat revizi" dialog, „QR štítek (PDF)" download. Bez fotek/historie revizí — odloženo do TASK 3.
- [x] Optimistic UI — `MudSnackbar` zprávy + okamžitý reload listu po mutacích.
- [x] **Bonus** — `/customers` + `/locations` stránky (CRUD), bez nich nelze vytvořit kotel. Zákazník dialog má ARES autofill.
- [x] **Bonus** — sidebar nav v `MainLayout` (mini drawer s ikonami pro Nástěnka/Zákazníci/Lokality/Kotle, hover-expand).
- [x] **Bonus** — danger zone collapse v detailu kotle (proti omylu).
- [ ] ~~E2E test (Playwright)~~ — odloženo. Setup browser binárek + nový pipeline = jiná session. Smoke test pokrývá manuální browser test.

---

## FÁZE 3 — Revizní zpráva a PDF (týdny 6–8)

### TASK 3.1 — Inspection form engine
- [x] Šablona jako JSON schema ([docs/templates/nv191_2022.json](templates/nv191_2022.json)) — 8 sekcí (obecné, palivo, hořák, spalinová cesta, pojistné zařízení, větrání, fotky, závěr) s typovanými poli (number/select/boolean/date/textarea/photo/signature).
- [x] Domain: `Inspection` aggregate (`Draft`/`Sign`/`Archive` invariants), `InspectionType` (annual_nv191, tpg704_01_service, emergency), `InspectionStatus` (Draft/Signed/Archived). Domain events `InspectionDrafted` + `InspectionSigned`.
- [x] EF konfigurace + migrace `AddInspections` s FORCE RLS, jsonb pro `form_data`, bytea pro signature.
- [x] API endpointy `/api/v1/inspections` — list (filter `boilerId`/`status`, cursor pagination), get, POST create draft, PUT `{id}/draft` update form/findings/recommendations.
- [x] FluentValidation validatory s českými hláškami.
- [x] Domain unit testy (9) + integrační testy (5: happy path, future date 400, missing boiler 404, cross-tenant 404, list filter).
- [ ] ~~Blazor DynamicForm~~ — odloženo do TASK 3.4 (sign flow UI). Engine je připravený přes JSON schema, Blazor renderer přijde s podpisem.
- [ ] ~~Autosave debounce 5s~~ — odloženo s DynamicForm.

### TASK 3.2 — PDF generator (QuestPDF)
- [x] `Infrastructure/Pdf/InspectionReportPdfRenderer.cs` + `InspectionReportBuilder.cs` (loaduje inspection + boiler + location + customer + technician + tenant a sestaví `InspectionReportData`).
- [x] Hlavička (název firmy + IČO/DIČ, číslo zprávy, typ revize), sekce (zákazník, lokalita, kotel, vyplněná data z `form_data`, závady, doporučení, podpisy).
- [x] Veřejný preview endpoint `GET /api/v1/inspections/{id}/preview.pdf`.
- [x] Integrační testy (3): PDF magic header, cross-tenant 404, smoke s customer name.
- [ ] ~~Font Noto Sans embedded~~ — odloženo. QuestPDF default font řeší českou diakritiku; pro tiskovou kvalitu s tenant-customizable hlavičkou přidáme v dalším kole.
- [ ] ~~PDF/A-2b compliance~~ — odloženo. QuestPDF nativně PDF/A nepodporuje; potřebovalo by post-processing přes Ghostscript. Zatím standardní PDF.
- [ ] ~~Snapshot test vs `tests/snapshots/inspection_example.pdf`~~ — odloženo. Binary snapshot flaky kvůli timestamps; lepší by bylo render-to-image diff. Pokrýváme přes magic header + size.

### TASK 3.3 — Backblaze B2 storage
- [x] `Application/Abstractions/IFileStorage.cs` + `StorageOptions` (Bucket, ServiceUrl, Region, AccessKey, SecretKey, ForcePathStyle).
- [x] `Infrastructure/Storage/S3FileStorage.cs` (sjednocená S3 impl pro Backblaze B2 i MinIO) přes AWS SDK 4.0.22.1.
- [x] Stream upload (Put/Get/Delete), AsyncLifetime resource cleanup.
- [x] Presigned URL TTL clamped (0; 7] dní (B2 limit).
- [x] Polly 8.6.6 retry 3× exponential s jitter na timeout/429/5xx.
- [x] Integrační test proti MinIO Testcontainers (5: put/get round-trip, missing → null, delete, presigned URL signature, TTL guard).
- [x] **Bonus** — `EnsureBucketExistsAsync()` helper pro idempotentní setup v CI/dev.
- [ ] ~~Key schema fixovaný v storage~~ — TASK 3.4 (sign flow ho používá: `tenants/{tenantId}/inspections/{year}/{inspectionId}.pdf`).

### TASK 3.4 — Inspection sign flow

#### 3.4a — Synchronní sign endpoint + audit log (hotovo)
- [x] `POST /api/v1/inspections/{id}/sign` (`InspectionEndpoints.SignAsync`).
- [x] Pipeline (synchronně): validate draft → render PDF → SHA256 → upload S3 (`tenants/{tenantId}/inspections/{year}/{inspectionId}.pdf`) → `Inspection.Sign()` → audit_log zápis → presigned download URL (TTL 7 dní).
- [x] `audit_log` tabulka + `AuditLog` entity (action, target, actor, IP, UA, metadata jsonb) s FORCE RLS + tenant_isolation policy.
- [x] `IFileStorage` registrované v Program.cs (`Storage:*` config) + MinIO sidecar v dev compose.
- [x] `AzKotleApiFactory` rozšířený o MinIO Testcontainer.
- [x] 7 integračních testů (happy path, audit log row, twice → 400, non-existent → 404, cross-tenant → 404, invalid base64 → 400, key convention) + 10 unit testů `AuditLog`.
- [ ] ~~Email odesílání + queue~~ — odloženo (vědomý skip pro MVP).
- [ ] ~~Background worker (Hangfire/Quartz)~~ — odloženo (synchronní pipeline < 3 s je pro MVP OK; přijde s emailem nebo ve Fázi 4).

#### 3.4b — Blazor DynamicForm + autosave (hotovo)
- [x] `<DynamicForm Schema Model OnChanged Disabled ExcludedSectionIds />` renderer pro `nv191_2022.json` — MudNumericField, MudSelect, MudCheckBox tri-state, MudTextField textarea, MudDatePicker.
- [x] Photo + signature jsou stub (placeholder MudAlert) — přijdou v 3.4c / další iteraci.
- [x] Stránka `/inspections/{id}` (InspectionEdit.razor): načte draft + schema + boiler kontext, render formuláře, `summary` sekce skipnutá (Findings/Recommendations/NextDueAt jako separátní MudTextField/MudDatePicker mapované na entity columns).
- [x] Autosave debounce 5 s (Task.Delay + CancellationTokenSource) + sticky save indikátor (Saved/Dirty/Saving/Error) + manuální „Uložit teď".
- [x] Read-only mode pro `Status != Draft` (zákaz editu, info banner, zobrazí signed_at + SHA-256).
- [x] Vstup z `/boilers/{id}` — tlačítko „Nová revize NV 191" → `POST /api/v1/inspections` → naviguje na `/inspections/{id}`. Smazán legacy `InspectionDialog` modal (per CLAUDE.md feedback „bez modálních oken").
- [x] InspectionsApiClient + InspectionSchemaClient v `Web.Client/Api/` + DI.
- [ ] ~~bUnit testy~~ — repo nemá bUnit, ručně otestovat v browseru.

#### 3.4c — UI „Podepsat a odeslat" (hotovo)
- [x] Aktivované „Podepsat a odeslat" tlačítko v `/inspections/{id}` → inline rozbalitelná sekce (per feedback „bez modálních oken") s warning seznamem nevyplněných required polí + signature canvas + „Potvrdit a podepsat" / „Zrušit".
- [x] Před otevřením sekce auto-flush nepoložených změn (sign jen toho, co je persistováno).
- [x] `<SignaturePadCanvas>` Razor komponenta — wrapper nad signature_pad@5.0.4 přes IJSObjectReference, per-element instance, high-DPI resize, `JSDisconnectedException` tolerance.
- [x] `wwwroot/js/signature-pad.js` ES module + `wwwroot/lib/signature_pad/signature_pad.umd.min.js` (vendored UMD, MIT).
- [x] `<script>` tag v `App.razor` (Web/Components) před blazor.web.js.
- [x] Po úspěšném sign: read-only mode + zelený success card s SignedAt, SHA-256 a tlačítkem „Stáhnout PDF" (`Nav.NavigateTo(presignedUrl, forceLoad: true)`).
- [x] Required-field check (`ComputeMissingRequiredFields`) iteruje schema sekce, photo+signature skipnuté, NextDueAt kontrolována zvlášť, zobrazí prvních 5 + „…".

---

## FÁZE 4 — Deploy a monitoring (týden 9) — hotovo

### TASK 4.1 — Production Dockerfile (commit `6d37b85`)
- [x] Multi-stage (SDK 10 → ASP.NET 10 alpine), `deploy/Dockerfile.{api,web}`.
- [x] Non-root user `azkotle` (uid 10001), TZ Europe/Prague, ASPNETCORE_URLS=http://+:8080.
- [x] HEALTHCHECK curl /health (api má i /health/ready s SELECT 1 proti DB).
- [ ] ~~`docker scan` bez HIGH/CRITICAL~~ — nezahrnuto, vyžaduje Docker Desktop nebo CI integraci, doporučeno ručně před prvním deployem.

### TASK 4.2 — docker-compose.prod.yml + Caddyfile (commit `6d37b85`)
- [x] Služby: app, api, postgres, redis, caddy, seq, postgres-backup. Internal + public sítě.
- [x] Caddyfile: auto-HTTPS přes Let's Encrypt pro `app.az-kotle.cz` + `api.az-kotle.cz`, HSTS 1y preload, security headers (X-Frame-Options, Permissions-Policy), apex redirect.
- [x] Postgres backup: vlastní sidecar (Alpine + pg_dump + mc), denně 03:00 → S3-compatible (B2), retention 14 dní.
- [x] Seq: SEQ_FIRSTRUN_ADMINPASSWORDHASH přes env, retention default Seq config (řízeno v UI).
- [x] `.env.prod.example` šablona všech secrets + bootstrap-vps.sh pro fresh Ubuntu 24 VPS.

### TASK 4.3 — GitHub Actions deploy (commit `f089a18`)
- [x] `.github/workflows/deploy.yml` — workflow_run trigger po úspěšném CI na main + workflow_dispatch.
- [x] Matrix build [api, web] → push do GHCR (sha-<short> + latest tagy), gha cache.
- [x] SSH deploy přes appleboy/ssh-action: git pull, edit .env.prod (AZKOTLE_IMAGE_TAG=sha-<new>), `compose pull api app`, `up -d --remove-orphans`, image prune.
- [x] Smoke test: curl /health, /health/ready (api), /health (web) — fail = workflow non-zero.
- [x] Rollback: ručně edit .env.prod AZKOTLE_IMAGE_TAG na previous sha + compose up.
- [ ] ~~Secrets `VPS_SSH_KEY`, `GHCR_TOKEN`~~ — `GITHUB_TOKEN` automatický, `VPS_HOST`/`VPS_USER`/`VPS_SSH_KEY` musí user nastavit v repo Settings.

### TASK 4.4 — Observability (commit `3f3ec41`)
- [x] Serilog v Api + Web (Console + Seq sink), Enrich Application/Environment, ReadFrom.Configuration.
- [x] Correlation ID middleware v Api: X-Correlation-ID header (validuje, gen fallback Guid 16 hex), push do Serilog LogContext.
- [x] UseSerilogRequestLogging — strukturované access logy.
- [x] prometheus-net.AspNetCore: UseHttpMetrics + MapMetrics → /metrics. Caddy blokuje navenek (path /metrics → 403).
- [x] Grafana dashboard skeleton `deploy/grafana/dashboards/overview.json` — 8 panelů (req rate, 5xx, in-flight, memory, duration percentiles, status code, GC, threadpool), template var `service`.
- [ ] ~~Sentry SDK~~ — vědomě skipnuté per user feedback (Serilog→Seq stačí).
- [ ] ~~Uptime monitoring (UptimeRobot/BetterStack)~~ — externí service, doporučeno user nastaví manuálně.
