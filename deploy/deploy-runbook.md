# AZ KOTLE — Production Deploy Runbook

Stav: **v1.0.0 ready** (audit findings #1 a #2 vyřešeny, viz CHANGELOG níže).

Cíl tohoto dokumentu: dát operátorovi vše potřebné pro **zero-touch nasazení** na čistý VPS i pro **zero-downtime update** běžící produkce.

---

## 1. Architecture v jedné větě

Caddy (TLS) → `api.az-kotle.cz` (ASP.NET Core minimal API) + `app.az-kotle.cz` (Blazor WASM) → Postgres 16 (RLS-enforced multi-tenancy) + S3-kompatibilní storage (PDF, fotky, loga) + Seq (logy) + Redis (cache, momentálně nevyužité).

Bezpečnostní invariant: runtime API se připojuje k DB jako `azkotle_app` (NOSUPERUSER NOBYPASSRLS) — RLS politiky `tenant_isolation` skutečně enforcují izolaci dat mezi tenanty. DDL migrace **nesmí** běžet z runtime kontejneru — odděluje je `migrator` sidecar.

---

## 2. Required environment variables (`.env.prod`)

Šablona: [`deploy/.env.prod.example`](.env.prod.example). Nikdy necommituj `.env.prod` — drž ho jen na VPS v `/opt/azkotle/deploy/.env.prod` s `chmod 600`.

| Klíč | Účel | Generování |
|------|------|-----------|
| `POSTGRES_DB` | Název databáze | typicky `azkotle` |
| `POSTGRES_USER` | DB superuser (init, migrace, backup) | typicky `azkotle` |
| `POSTGRES_PASSWORD` | Heslo superusera | `openssl rand -base64 32` (≥ 24 znaků) |
| `APP_DB_USER` | Runtime DB role | `azkotle_app` (default) |
| `APP_DB_PASSWORD` | Heslo runtime role | `openssl rand -base64 32` |
| `AZKOTLE_JWT_SECRET` | HMAC SHA-256 secret pro JWT | `openssl rand -base64 48` (≥ 32 znaků) |
| `STORAGE_BUCKET` | S3 bucket pro PDF a fotky | `azkotle-prod` |
| `STORAGE_SERVICE_URL` | S3 endpoint | `https://s3.eu-central-003.backblazeb2.com` (B2) |
| `STORAGE_REGION` | S3 region | `eu-central-003` |
| `STORAGE_ACCESS_KEY` | B2 application key id | z B2 console |
| `STORAGE_SECRET_KEY` | B2 application key secret | z B2 console |
| `BACKUP_BUCKET` | Bucket pro pg_dump zálohy | volitelně oddělený, default = `STORAGE_BUCKET` |
| `BACKUP_RETENTION_DAYS` | Doba retence záloh | `14` |
| `BACKUP_CRON` | Schedule pg_dump | `0 3 * * *` (denně 03:00 Europe/Prague) |
| `SEQ_ADMIN_PASSWORD_HASH` | Seq admin hash | `docker run --rm datalust/seq config hash '<heslo>'` |
| `SEQ_API_KEY` | Seq write api key | volitelné, vytvořit v Seq UI po deploy |
| `AZKOTLE_IMAGE_TAG` | Image tag | `latest` nebo SHA z CI |

**Compose fail-fast invariant:** klíče s `:?` v compose (`POSTGRES_PASSWORD`, `APP_DB_PASSWORD`, `AZKOTLE_JWT_SECRET`) způsobí, že `docker compose up` selže okamžitě pokud chybí. Žádné spuštění s "default" hesly.

---

## 3. First-time deploy na čistý VPS

```bash
# 1. SSH na VPS, naklonuj repo
ssh root@vps.az-kotle.cz
git clone https://github.com/turkott/azkotle.git /opt/azkotle
cd /opt/azkotle/deploy

# 2. Vyplň .env.prod
cp .env.prod.example .env.prod
chmod 600 .env.prod
$EDITOR .env.prod
#   - vygeneruj POSTGRES_PASSWORD, APP_DB_PASSWORD, AZKOTLE_JWT_SECRET
#   - vyplň STORAGE_*  (B2 application key)
#   - vyplň SEQ_ADMIN_PASSWORD_HASH

# 3. DNS: nastav A/AAAA záznamy
#   app.az-kotle.cz   → <VPS public IP>
#   api.az-kotle.cz   → <VPS public IP>
#   az-kotle.cz       → <VPS public IP>
# Bez DNS Caddy nedokáže vystavit Let's Encrypt cert.

# 4. Pull images
docker compose -f docker-compose.prod.yml --env-file .env.prod pull

# 5. Up — Compose orchestruje:
#   postgres (healthy)
#   → migrator (runs migrations as POSTGRES_USER, exits 0)
#   → api + app start
#   → caddy issues Let's Encrypt cert + routes traffic
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d

# 6. Sleduj migrace
docker compose -f docker-compose.prod.yml logs migrator
# Očekáváno: "Applying N migration(s): ... Migrations applied successfully."

# 7. Smoke test
curl -fsS https://api.az-kotle.cz/health
curl -fsS https://api.az-kotle.cz/health/ready
# /health/ready vrací { "status": "ready", "checks": { "db": "ok", "s3": "ok" } }

curl -fsS https://app.az-kotle.cz/ | grep -q "AZ KOTLE"

# 8. První registrace přes UI: https://app.az-kotle.cz/register
```

---

## 4. Update / nový release

Image se buildnou v CI a publishnou do `ghcr.io/turkott/azkotle-{api,web}:<tag>`.

```bash
ssh root@vps.az-kotle.cz
cd /opt/azkotle/deploy

# 1. Volitelně specifikuj tag (default = latest)
echo "AZKOTLE_IMAGE_TAG=v1.2.0" >> .env.prod  # nebo edituj manuálně

# 2. Pull new images
docker compose -f docker-compose.prod.yml --env-file .env.prod pull

# 3. Re-run migrator (idempotent — pokud nejsou pending migrace, exit 0 instantně)
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --force-recreate migrator
docker compose -f docker-compose.prod.yml --env-file .env.prod logs migrator --tail=50
# Pokud migrator selže (exit != 0), DON'T pokračuj — fix migrations + redeploy.

# 4. Recreate api + app (Compose je restartuje s novými images)
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --force-recreate api app

# 5. Smoke
curl -fsS https://api.az-kotle.cz/health/ready
docker compose logs api --tail=100 | grep -iE 'error|fatal'
```

**Zero-downtime caveat:** stávající Compose nemá rolling-update — `up -d --force-recreate api` má krátký down moment (~3-5 s). Pro skutečně zero-downtime by bylo třeba dvoukontejnerový pattern + Caddy reload. Mimo MVP scope.

---

## 5. Rollback procedura

### Migrace selhala (`migrator` exit != 0)

API/app se nespustí (Compose `condition: service_completed_successfully` halt). Operátor:

1. `docker compose logs migrator` — najít chybu (DDL conflict, FK violation, …)
2. Opravit migration source v repu, rebuild image, redeploy. NIKDY ručně needitovat již-aplikované migrace.
3. Pro obnovení po commit-pak-rollback DDL: `pg_restore` z poslední `postgres-backup` zálohy (B2 bucket `BACKUP_BUCKET`).

### Runtime regrese po deploy

Předchozí image tag stále existuje v ghcr.

```bash
# .env.prod: nastav AZKOTLE_IMAGE_TAG zpět na předchozí verzi (např. v1.1.5)
docker compose -f docker-compose.prod.yml --env-file .env.prod pull
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --force-recreate api app
```

**Pozor:** rollback image NEODPLIKUJE migrace, které mezitím proběhly. Pokud nový release přidal sloupec, který starý kód nepoužívá, je to safe. Pokud nový release změnil schéma incompatibly (rename/drop), je třeba nejdřív DB rollback (z backup) a teprve pak image rollback.

---

## 6. Health check semantics

| Endpoint | Účel | Auth | Použití |
|----------|------|------|---------|
| `GET /health` | Kestrel je naživu | none | Compose container healthcheck |
| `GET /health/ready` | DB + S3 dostupné | none | Caddy / load balancer readiness |

`/health/ready` vrací 200 jen když DB i S3 odpoví. Při degradaci jednoho komponentu vrátí 503 + JSON s `checks: { db: <status>, s3: <status> }` — operátor okamžitě ví která dependence padla bez grep'ování logů.

---

## 7. Caddy compression invariants

Caddyfile používá `encode zstd gzip` na obou subdoménách. Defaultní whitelist Caddy v2 zahrnuje `application/wasm`, `application/json`, `text/*`, `application/javascript`. **Nemodifikovat** bez záměrného důvodu.

Blazor WASM optimization: ASP.NET Core `app.MapStaticAssets()` ([src/AzKotle.Web/Program.cs:58](../src/AzKotle.Web/Program.cs#L58)) servuje pre-compressed assety (`.dll.br`, `.wasm.br`, `.dat.br`) generované při `dotnet publish`. Caddy je passes-through bez recompresse (Content-Encoding: br header). Výsledek:
- Initial WASM bundle ~2.5 MB → ~600 KB on wire (brotli)
- DLLs reused mezi releases díky stable hash naming

Browser DevTools → Network → filter "wasm" by měl ukázat `Content-Encoding: br` u všech `.dll`/`.wasm` requestů.

---

## 8. Monitoring & logs

| Co | Kde | Jak |
|----|-----|-----|
| Application logs | Seq @ http://seq:80 (internal) | `ssh -L 8081:localhost:80 root@vps` → http://localhost:8081 |
| Container logs | docker logs | `docker compose logs <service> --tail=100 -f` |
| Prometheus metrics | `:8080/metrics` (api/app) | Caddy blokuje navenek; scrape přes internal network |
| Grafana dashboard skeleton | `deploy/grafana/dashboards/overview.json` | Manuálně import po Grafana setup |
| Audit log | Postgres tabulka `audit_log` | Append-only (DB trigger ENFORCE-S immutability), per-tenant RLS |
| pg_dump zálohy | B2 bucket `BACKUP_BUCKET/<prefix>/` | Cron `0 3 * * *`, retence 14 dní |

Při incidentu: `docker compose logs api --since 1h` → grep `error|fatal|warn` + correlation IDs (`X-Correlation-ID` header v každém requestu).

---

## 9. Smoke test — pre-launch i po každém deployi

Integration test [`tests/AzKotle.Api.IntegrationTests/Smoke/GoLiveSmokeTests.cs`](../tests/AzKotle.Api.IntegrationTests/Smoke/GoLiveSmokeTests.cs) drives full happy path: register company → customer → boiler → draft NV 191 inspekce s mock S3 photo key → sign → public viewer GET. Sign trigger'uje `InspectionReportBuilder` + QuestPDF rendering proti Linux containeru.

```bash
# Lokálně (vyžaduje Docker daemon pro Postgres + MinIO testcontainers):
dotnet test tests/AzKotle.Api.IntegrationTests --filter "Category=Smoke"
```

**Před každým prod deployi spustit.** Pokud failne, NEPUBLIKOVAT image — opravit a re-buildit.

---

## 10. Bezpečnost & compliance

- **GDPR:** PII (jména zákazníků, adresy) jsou per-tenant scoped přes Postgres RLS. Backup zálohy v B2 jsou per-tenant nerozdělené — při GDPR delete request je třeba retain backup po `BACKUP_RETENTION_DAYS=14`, pak je delete kompletní.
- **Rate limiting:**
  - `/api/v1/auth/*` — 5 req/min/IP (Argon2id ochrana)
  - `/api/v1/public/inspections/*` — 30 req/min/IP (F17 brute-force defense pro 192-bit access_hash)
  - Authenticated endpoints — žádný limit (per-tenant quota by byl logical follow-up)
- **HTTPS:** auto-issue Let's Encrypt přes Caddy. HSTS 1y + preload — žádný HTTP fallback po prvním kontaktu.
- **CORS:** whitelist `https://app.az-kotle.cz` + `https://az-kotle.cz`. Cross-origin requesty odjinud zamítnuty.
- **Secrets:** žádný hardcoded v repu. `appsettings.json` má prázdný JWT secret → fail-closed při missing env var. `.env.prod` na VPS s `chmod 600`.

---

## 11. Recent CHANGELOG (pre-1.0.0 hardening)

- **Finding #1 vyřešen:** přidán `migrator` sidecar do `docker-compose.prod.yml`. Spouští `dotnet AzKotle.Api.dll --apply-migrations` jako `POSTGRES_USER` (superuser), api/app čekají přes `condition: service_completed_successfully`. Runtime API zůstává jako `azkotle_app` (NOSUPERUSER) → RLS plně enforcuje.
- **Finding #2 vyřešen:** `Dockerfile.api` přidává `fontconfig` a `ttf-dejavu` (~5 MB). QuestPDF má bundled Lato pro Czech glyphs, ale system fonts jako defense-in-depth proti budoucím renderer regresím.
- **`/health/ready` enhanced:** kontroluje teď DB **i** S3. JSON response `{ status, checks: { db, s3 } }` umožní okamžitou diagnostiku bez log inspection.
- **`IFileStorage.HeadBucketAsync`:** nový kontrakt method na probe S3 dostupnosti. Implementuje S3FileStorage přes `AmazonS3.HeadBucketAsync`.

---

## Quick reference

```bash
# Status
docker compose -f docker-compose.prod.yml --env-file .env.prod ps

# Logs
docker compose logs api --tail=200 -f

# Restart single service
docker compose up -d --force-recreate api

# Manual migration (debug)
docker compose run --rm migrator

# DB shell
docker compose exec postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"

# Connect as runtime user (verify RLS works)
docker compose exec postgres psql -U azkotle_app -d "$POSTGRES_DB"
# → SELECT * FROM inspections;  -- vrátí 0 řádků (RLS deny-all bez tenant context)
```
