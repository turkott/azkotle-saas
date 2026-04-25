#!/bin/bash
# Bootstrap skript pro nový VPS (Ubuntu 24.04).
# Spustí se ručně jednou, instaluje Docker + clone repo + první nastavení.
#
# Použití (jako root nebo s sudo):
#   curl -fsSL https://raw.githubusercontent.com/turkott/azkotle-saas/main/deploy/bootstrap-vps.sh | sudo bash
#   ALEBO scp + ssh:
#   scp deploy/bootstrap-vps.sh root@80.211.223.147:/tmp/
#   ssh root@80.211.223.147 'bash /tmp/bootstrap-vps.sh'

set -euo pipefail

REPO_URL="${REPO_URL:-https://github.com/turkott/azkotle-saas.git}"
REPO_DIR="${REPO_DIR:-/opt/azkotle}"

if [[ "$EUID" -ne 0 ]]; then
    echo "Spusť jako root nebo přes sudo." >&2
    exit 1
fi

echo "[bootstrap] aktualizace systému…"
apt-get update -y
apt-get upgrade -y
apt-get install -y curl git ca-certificates ufw

echo "[bootstrap] instalace Dockeru přes get.docker.com…"
if ! command -v docker >/dev/null 2>&1; then
    curl -fsSL https://get.docker.com | sh
fi
systemctl enable --now docker

echo "[bootstrap] firewall (UFW) — povol SSH + HTTP/HTTPS, deny default…"
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable

echo "[bootstrap] git clone do ${REPO_DIR}…"
if [[ -d "${REPO_DIR}/.git" ]]; then
    git -C "${REPO_DIR}" pull --ff-only
else
    git clone "${REPO_URL}" "${REPO_DIR}"
fi

cd "${REPO_DIR}/deploy"

if [[ ! -f .env.prod ]]; then
    cp .env.prod.example .env.prod
    chmod 600 .env.prod
    echo
    echo "===================================================================="
    echo "  Vytvořen ${REPO_DIR}/deploy/.env.prod ze šablony."
    echo "  PŘED prvním 'docker compose up' edituj a vyplň skutečné secrets:"
    echo "    POSTGRES_PASSWORD, AZKOTLE_JWT_SECRET, STORAGE_*, SEQ_*"
    echo "  Pak spusť:"
    echo "    cd ${REPO_DIR}/deploy"
    echo "    docker compose -f docker-compose.prod.yml --env-file .env.prod up -d"
    echo "===================================================================="
else
    echo "[bootstrap] .env.prod už existuje, neměním."
fi

echo "[bootstrap] hotovo."
