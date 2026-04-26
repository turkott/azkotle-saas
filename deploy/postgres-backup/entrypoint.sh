#!/bin/bash
# Spustí backup.sh denně ve 03:00 (cron) a tail logs.
# BACKUP_CRON env var může přepsat schedule.

set -euo pipefail

: "${BACKUP_CRON:=0 3 * * *}"

# Při startu udělej smoke run, ať vidíme případnou chybu hned (ne až po 24 h).
if [[ "${BACKUP_RUN_ON_START:-false}" == "true" ]]; then
    /usr/local/bin/backup.sh || echo "[entrypoint] startup backup failed (continuing to schedule)"
fi

# Cron crontab: env vars předáváme přes nativní `KEY=value` řádky NAHOŘE
# v crontab souboru (POSIX cron + busybox crond standardní syntax). Cron sám
# je nastaví jako env pro spouštěné jobs — žádný shell sourcing, žádné
# escapování speciálních znaků jako u `. /etc/environment`.
#
# Bug před tímto fixem: `env > /etc/environment; cron sourcuje to via dot`
# rozbil `BACKUP_CRON=0 3 * * *` (mezery → "3: not found") a riskoval ztrátu
# hodnot s shell-special chars (+, %, $, ', ") ve S3 secretech.
#
# Cron format note: hodnoty NESMÍ obsahovat newline (cron parsing). Pro
# S3 secret keys to není problém (jsou jednořádkové). Komentář `#` na
# začátku řádku je v cronu komentář, ale `#` UVNITŘ hodnoty se cronem
# bere jako součást value (testováno).
{
    env | grep -E '^(POSTGRES_|STORAGE_|BACKUP_)='
    echo "${BACKUP_CRON} /usr/local/bin/backup.sh >> /var/log/backup.log 2>&1"
} > /etc/crontabs/root

touch /var/log/backup.log
echo "[entrypoint] cron schedule: ${BACKUP_CRON}"
crond -f -l 8 &
tail -F /var/log/backup.log
