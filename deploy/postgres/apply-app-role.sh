#!/bin/bash
# Manuální runner pro EXISTUJÍCÍ produkční postgres volume.
#
# Init scripty v /docker-entrypoint-initdb.d/ se na již vytvořeném volume
# nespustí (Postgres image je spouští jen při prvním vytvoření datového
# adresáře). Tento skript zkopíruje init script do běžícího kontejneru
# a spustí ho přes docker compose exec.
#
# Použití (z /opt/azkotle/deploy nebo lokálně z deploy/):
#   ./postgres/apply-app-role.sh
#
# Vyžaduje:
#   - běžící compose stack (docker compose up -d)
#   - .env.prod s vyplněnými POSTGRES_* a APP_DB_PASSWORD
#
# Po úspěšném běhu MUSÍ následovat restart api kontejneru, aby použil novou roli:
#   docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --force-recreate api

set -euo pipefail

cd "$(dirname "$0")/.."
DEPLOY_DIR="$(pwd)"

COMPOSE_FILE="${DEPLOY_DIR}/docker-compose.prod.yml"
ENV_FILE="${DEPLOY_DIR}/.env.prod"
INIT_SCRIPT="${DEPLOY_DIR}/postgres/init/01-create-app-role.sh"

if [[ ! -f "$ENV_FILE" ]]; then
    echo "FATAL: $ENV_FILE neexistuje. Spusť bootstrap-vps.sh nebo zkopíruj .env.prod.example." >&2
    exit 1
fi

if [[ ! -f "$INIT_SCRIPT" ]]; then
    echo "FATAL: $INIT_SCRIPT neexistuje." >&2
    exit 1
fi

# Extrakce konkrétních klíčů přes grep — NEpoužívat `set -a; . "$ENV_FILE"`,
# protože .env.prod obsahuje hodnoty s nekvótovanými mezerami (typicky
# BACKUP_CRON=0 3 * * *), které by bash interpretoval jako příkazy. To se
# reálně stalo při prvním běhu Iterace 1.1 — viz commit history.
extract() {
    grep "^${1}=" "$ENV_FILE" | head -1 | cut -d= -f2-
}

APP_DB_PASSWORD=$(extract APP_DB_PASSWORD)
POSTGRES_USER=$(extract POSTGRES_USER)
POSTGRES_DB=$(extract POSTGRES_DB)

if [[ -z "${APP_DB_PASSWORD}" ]]; then
    echo "FATAL: APP_DB_PASSWORD není v ${ENV_FILE}." >&2
    echo "       Vygeneruj: openssl rand -base64 32" >&2
    exit 1
fi
if [[ -z "${POSTGRES_USER}" || -z "${POSTGRES_DB}" ]]; then
    echo "FATAL: POSTGRES_USER nebo POSTGRES_DB chybí v ${ENV_FILE}." >&2
    exit 1
fi

echo "[apply-app-role] copying init script into postgres container..."
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" cp \
    "$INIT_SCRIPT" postgres:/tmp/01-create-app-role.sh

echo "[apply-app-role] running script as ${POSTGRES_USER}..."
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" exec -T \
    -e APP_DB_PASSWORD="$APP_DB_PASSWORD" \
    -e POSTGRES_USER="$POSTGRES_USER" \
    -e POSTGRES_DB="$POSTGRES_DB" \
    postgres bash /tmp/01-create-app-role.sh

echo "[apply-app-role] cleaning up tmp script in container..."
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" exec -T postgres \
    rm -f /tmp/01-create-app-role.sh

echo "[apply-app-role] verifying azkotle_app role attributes..."
docker compose -f "$COMPOSE_FILE" --env-file "$ENV_FILE" exec -T \
    postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
    -c "SELECT rolname, rolsuper, rolbypassrls, rolcanlogin FROM pg_roles WHERE rolname = 'azkotle_app';"

echo
echo "==============================================================================="
echo "  azkotle_app role je připravená."
echo
echo "  DALŠÍ KROK — restart API kontejneru, aby použil novou DB roli:"
echo "    docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --force-recreate api"
echo
echo "  Pak ověř:"
echo "    curl -fsS https://api.az-kotle.cz/health/ready"
echo "    docker compose logs api --tail=100 | grep -iE 'error|warn|fatal'"
echo
echo "  ROLLBACK (kdyby /health/ready vrátil 503):"
echo "    Vrať starou Username v .env.prod (nebo přepiš ConnectionStrings__AzKotleDb"
echo "    na původní účet) a znovu --force-recreate api."
echo "==============================================================================="
