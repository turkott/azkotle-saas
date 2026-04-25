#!/bin/bash
# Spustí backup.sh denně ve 03:00 (cron) a tail logs.
# BACKUP_CRON env var může přepsat schedule.

set -euo pipefail

: "${BACKUP_CRON:=0 3 * * *}"

# Při startu udělej smoke run, ať vidíme případnou chybu hned (ne až po 24 h).
if [[ "${BACKUP_RUN_ON_START:-false}" == "true" ]]; then
    /usr/local/bin/backup.sh || echo "[entrypoint] startup backup failed (continuing to schedule)"
fi

# crontab: vlož z env, env vars exportujeme do souboru ať je cron vidí.
env | grep -E '^(POSTGRES_|STORAGE_|BACKUP_)' > /etc/environment

cat > /etc/crontabs/root <<EOF
${BACKUP_CRON} . /etc/environment; /usr/local/bin/backup.sh >> /var/log/backup.log 2>&1
EOF

touch /var/log/backup.log
echo "[entrypoint] cron schedule: ${BACKUP_CRON}"
crond -f -l 8 &
tail -F /var/log/backup.log
