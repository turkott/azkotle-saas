# AZ KOTLE — Production Deployment

Provozní příručka pro produkční deploy AZ KOTLE SaaS na **Forpsi VPS** (Ubuntu 24, IP `80.211.223.147`, hostname `AA-kotle`).

Deploy je primárně **automatizovaný přes GitHub Actions** (push na `main` → CI → Deploy workflow). Tento dokument popisuje **manuální operace** pro ladění, hotfixy a údržbu.

## Topologie

| Doména | Cíl | Poznámka |
|---|---|---|
| `https://www.az-kotle.cz` | `app` container (Blazor) | Primary |
| `https://app.az-kotle.cz` | `app` container | Alias pro staré bookmarky |
| `https://api.az-kotle.cz` | `api` container (.NET API) | |
| `https://az-kotle.cz` | 301 → www | |

Reverse proxy: Caddy s auto-HTTPS přes Let's Encrypt.

## Containery

| Service | Image | Role |
|---|---|---|
| `postgres` | `postgres:16-alpine` | DB, persistentní volume `postgres_data` |
| `redis` | `redis:7-alpine` | Session store, rate limiting |
| `seq` | `datalust/seq:latest` | Centralized logs (interní) |
| `migrator` | `ghcr.io/turkott/azkotle-api:<tag>` | One-shot DDL migrace (POSTGRES_USER, superuser) |
| `api` | `ghcr.io/turkott/azkotle-api:<tag>` | API runtime (azkotle_app, NOSUPERUSER) |
| `app` | `ghcr.io/turkott/azkotle-web:<tag>` | Blazor frontend |
| `caddy` | `caddy:2-alpine` | Reverse proxy + auto-HTTPS |
| `postgres-backup` | local build | Denní pg_dump → Backblaze B2 |

## VPS layout

- `/opt/azkotle/` — git clone repa (CI sem fetchuje na `main`)
- `/opt/azkotle/deploy/.env.prod` — secrets (POSTGRES_PASSWORD, AZKOTLE_JWT_SECRET, STORAGE_*, atd.) — **nikdy v gitu**
- `/opt/azkotle/deploy/Caddyfile` — Caddy config (zdrojový soubor, do containeru bind-mountovaný RO)

## Manuální deploy (pokud CI selže)

Předpoklad: SSH klíč `~/.ssh/azkotle_ed25519` na lokálu.

```bash
# Připojení na VPS
ssh -i ~/.ssh/azkotle_ed25519 root@80.211.223.147

# Na VPS:
cd /opt/azkotle

# Stažení změn z GitHubu (CI standardně dělá git fetch + reset --hard origin/main)
git pull origin main

# Pull nových images z GHCR (CI je tam pushuje při buildu)
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod pull api app

# Restart stacku — depends_on zajistí pořadí: postgres → migrator → api/app → caddy
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod up -d --remove-orphans

# Smoke test
curl -fsS https://api.az-kotle.cz/health/ready
curl -fsS -o /dev/null -w "%{http_code}\n" https://www.az-kotle.cz/
```

## Lokální build images (alternativa k GHCR)

Pokud potřebuješ buildit images **přímo na VPS** (např. testování necommitnutých změn):

```bash
cd /opt/azkotle

docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod build api app
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod up -d --no-deps api app
```

> **Standardní cesta je přes CI** — GitHub Actions buildí images a pushuje na `ghcr.io/turkott/azkotle-{api,web}:sha-<short>`. Lokální build na VPS je **fallback pro emergency**.

## Logy

```bash
# Sledování konkrétní service v reálném čase
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod logs -f api

# Posledních 100 řádků api + app
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod logs --tail 100 api app

# Centralizované logy přes Seq UI (SSH tunel)
ssh -i ~/.ssh/azkotle_ed25519 -L 8081:localhost:80 root@80.211.223.147
# pak otevři http://localhost:8081
```

## Migrace databáze

Migrace běží **automaticky** v `migrator` sidecaru při každém deployi (před spuštěním api/app). Sidecar používá `POSTGRES_USER` (superuser, DDL-capable). Runtime api/app **vždy** jede pod `azkotle_app` (NOSUPERUSER NOBYPASSRLS) — superuser by tichá obcházel `FORCE ROW LEVEL SECURITY` a tenanti by viděli data ostatních.

Manuální spuštění (pokud sidecar ze zázračných důvodů vynechal):

```bash
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod up -d --force-recreate migrator
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod logs migrator --tail 30
```

## Backup databáze

`postgres-backup` container běží denně v 03:00 (UTC) a uploaduje pg_dump na Backblaze B2 (bucket z `STORAGE_BUCKET` env var). Retention: 14 dní.

```bash
# Manuální spuštění zálohy (mimo cron):
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod exec postgres-backup /backup.sh

# Ad-hoc dump na lokál (bez B2):
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod exec -T postgres \
  pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom \
  > "azkotle-$(date +%Y%m%d-%H%M).dump"

# Obnova z dumpu (POZOR — destruktivní):
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod exec -T postgres \
  pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists < azkotle-20260427-1430.dump
```

## Reload Caddy (po editu Caddyfile)

```bash
docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
  exec -T caddy caddy reload --config /etc/caddy/Caddyfile
```

## Secrets a `.env.prod`

`deploy/.env.prod` na VPS obsahuje produkční secrets a **nikdy nesmí být v gitu**. `.gitignore` má pravidlo `.env.*` s výjimkou `*.example`. Šablona je v [deploy/.env.prod.example](deploy/.env.prod.example).

Při přidání nového env varu:
1. Přidej do `deploy/.env.prod.example` s placeholder hodnotou (popř. komentářem)
2. Upraving compose `${VAR_NAME:?error message}`
3. Přidej skutečnou hodnotu na VPS do `/opt/azkotle/deploy/.env.prod` ručně přes SSH (NE přes git)

## Health checks

```bash
# Z venku
curl -fsS https://api.az-kotle.cz/health/ready
# {"status":"ready","checks":{"db":"ok","s3":"probe_unavailable"}}
# Pozn.: s3 = "probe_unavailable" je informational only — Backblaze B2 nepodporuje
# HeadBucket, ale upload/download funguje.

curl -fsS -o /dev/null -w "%{http_code}\n" https://www.az-kotle.cz/
# 200

# Z VPS (interní healthcheck stejný jako Docker HEALTHCHECK)
ssh -i ~/.ssh/azkotle_ed25519 root@80.211.223.147 \
  "docker compose -f /opt/azkotle/deploy/docker-compose.prod.yml ps"
```

## CI/CD workflow

| Trigger | Workflow | Akce |
|---|---|---|
| Push na `main` | `.github/workflows/ci.yml` | build, unit tests, integration tests, format check |
| CI green | `.github/workflows/deploy.yml` (workflow_run) | build & push images na GHCR, SSH deploy na VPS |

Sledování:
```bash
gh run list --branch main --limit 5
gh run watch <run_id> --exit-status
gh run view <run_id> --log-failed | tail -50
```

GitHub Secrets (nutné pro deploy workflow):
- `VPS_HOST` — `80.211.223.147`
- `VPS_USER` — `root`
- `VPS_SSH_KEY` — privátní klíč (matching `~/.ssh/azkotle_ed25519.pub` na VPS)

## Rollback

Pokud nový deploy něco rozbije:

```bash
# Najdi předchozí funkční SHA tag
ssh -i ~/.ssh/azkotle_ed25519 root@80.211.223.147 \
  "docker images ghcr.io/turkott/azkotle-api --format '{{.Tag}}' | head -10"

# Pin tag v compose env override a restart
ssh -i ~/.ssh/azkotle_ed25519 root@80.211.223.147 << 'EOF'
cd /opt/azkotle
AZKOTLE_IMAGE_TAG=sha-ef64f6e \
  docker compose -f deploy/docker-compose.prod.yml --env-file deploy/.env.prod \
  up -d --no-deps api app
EOF
```

Trvalý rollback: `git revert <commit>` lokálně, push → CI redeploy.
