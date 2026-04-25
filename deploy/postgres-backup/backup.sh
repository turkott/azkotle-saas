#!/bin/bash
# Postgres backup → S3-compatible storage (Backblaze B2).
#
# Required env vars:
#   POSTGRES_HOST      - hostname of postgres container (default: postgres)
#   POSTGRES_PORT      - port (default: 5432)
#   POSTGRES_DB        - database name
#   POSTGRES_USER      - username
#   POSTGRES_PASSWORD  - password
#   STORAGE_SERVICE_URL  - S3 endpoint (e.g. https://s3.eu-central-003.backblazeb2.com)
#   STORAGE_REGION       - region (e.g. eu-central-003)
#   STORAGE_ACCESS_KEY   - B2 application key id
#   STORAGE_SECRET_KEY   - B2 application key secret
#   STORAGE_BUCKET       - destination bucket
# Optional:
#   BACKUP_PREFIX        - object key prefix (default: backups/postgres)
#   BACKUP_RETENTION_DAYS - delete dumps older than N days (default: 14)

set -euo pipefail

: "${POSTGRES_HOST:=postgres}"
: "${POSTGRES_PORT:=5432}"
: "${BACKUP_PREFIX:=backups/postgres}"
: "${BACKUP_RETENTION_DAYS:=14}"

for var in POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD \
           STORAGE_SERVICE_URL STORAGE_REGION STORAGE_ACCESS_KEY STORAGE_SECRET_KEY STORAGE_BUCKET; do
    if [[ -z "${!var:-}" ]]; then
        echo "[backup] FATAL: env var $var není nastavená" >&2
        exit 1
    fi
done

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
key="${BACKUP_PREFIX}/${POSTGRES_DB}-${timestamp}.sql.gz"

mc alias set b2 "${STORAGE_SERVICE_URL}" "${STORAGE_ACCESS_KEY}" "${STORAGE_SECRET_KEY}" \
    --api S3v4 >/dev/null

echo "[backup] $(date -Iseconds) start: ${POSTGRES_HOST}:${POSTGRES_PORT}/${POSTGRES_DB} → b2/${STORAGE_BUCKET}/${key}"

PGPASSWORD="${POSTGRES_PASSWORD}" pg_dump \
    --host="${POSTGRES_HOST}" --port="${POSTGRES_PORT}" \
    --username="${POSTGRES_USER}" --dbname="${POSTGRES_DB}" \
    --format=plain --no-owner --no-privileges \
    | gzip -9 \
    | mc pipe "b2/${STORAGE_BUCKET}/${key}"

echo "[backup] $(date -Iseconds) done: ${key}"

# Retention: list old objects under prefix and delete those older than N days.
if [[ "${BACKUP_RETENTION_DAYS}" -gt 0 ]]; then
    echo "[backup] retention: removing dumps older than ${BACKUP_RETENTION_DAYS} days"
    mc find "b2/${STORAGE_BUCKET}/${BACKUP_PREFIX}" \
        --older-than "${BACKUP_RETENTION_DAYS}d" \
        --exec "mc rm {}" || true
fi
