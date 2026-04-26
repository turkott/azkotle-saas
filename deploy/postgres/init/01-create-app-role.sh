#!/bin/bash
# Vytvoří non-superuser runtime roli azkotle_app pro AZ KOTLE API.
#
# Spouští se DVĚMA způsoby:
#  1) Automaticky při PRVNÍM vytvoření postgres volume (Docker postgres image
#     spustí všechny .sh / .sql v /docker-entrypoint-initdb.d/ v abecedním pořadí).
#  2) Manuálně proti EXISTUJÍCÍ produkční DB přes deploy/postgres/apply-app-role.sh
#     (init scripty se na již vytvořeném volume nespustí).
#
# Idempotentní: opakované spuštění jen aktualizuje heslo + práva, nezpůsobí chybu.
#
# Vyžadované env vars:
#   APP_DB_PASSWORD  — heslo pro novou roli azkotle_app (≥ 24 znaků)
#   POSTGRES_USER    — admin role (default 'postgres', když nastaví Docker init)
#   POSTGRES_DB      — název databáze (default 'azkotle')

set -euo pipefail

if [[ -z "${APP_DB_PASSWORD:-}" ]]; then
    echo "[01-create-app-role] FATAL: APP_DB_PASSWORD env var není nastavená." >&2
    exit 1
fi

PGUSER="${POSTGRES_USER:-postgres}"
PGDB="${POSTGRES_DB:-azkotle}"

echo "[01-create-app-role] running as ${PGUSER} against database ${PGDB}"

psql -v ON_ERROR_STOP=1 --username "$PGUSER" --dbname "$PGDB" <<-EOSQL
DO \$do\$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'azkotle_app') THEN
        EXECUTE format('CREATE ROLE azkotle_app LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS', '$APP_DB_PASSWORD');
        RAISE NOTICE 'Created role azkotle_app';
    ELSE
        EXECUTE format('ALTER ROLE azkotle_app WITH LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS', '$APP_DB_PASSWORD');
        RAISE NOTICE 'Updated existing role azkotle_app (rotated password, ensured NOSUPERUSER NOBYPASSRLS)';
    END IF;
END \$do\$;

GRANT CONNECT ON DATABASE "$PGDB" TO azkotle_app;
GRANT USAGE ON SCHEMA public TO azkotle_app;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO azkotle_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO azkotle_app;

-- Default privileges pro tabulky/sequences vytvořené budoucími migracemi
-- (migrace běží jako $PGUSER / superuser).
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO azkotle_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO azkotle_app;

-- Audit log: append-only.
-- Pokud audit_log ještě neexistuje (běží před první migrací), REVOKE skipneme;
-- skript musí být spuštěn znovu po migracích, které tabulku vytvoří. Pro
-- dlouhodobou imutabilitu doporučeno přidat trigger v samostatné migraci.
DO \$do\$ BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_class
        WHERE relname = 'audit_log' AND relnamespace = 'public'::regnamespace
    ) THEN
        REVOKE UPDATE, DELETE ON public.audit_log FROM azkotle_app;
        RAISE NOTICE 'Revoked UPDATE, DELETE on audit_log from azkotle_app (append-only)';
    ELSE
        RAISE NOTICE 'Table audit_log does not exist yet — re-run this script after migrations to enforce append-only';
    END IF;
END \$do\$;
EOSQL

echo "[01-create-app-role] OK"
