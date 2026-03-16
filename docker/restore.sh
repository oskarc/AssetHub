#!/usr/bin/env bash
# ============================================================================
# AssetHub — Restore Script
# ============================================================================
# Restores a backup created by backup.sh.
#
# Usage:
#   ./docker/restore.sh ./backups/20260314_120000
#   COMPOSE_FILE=docker/docker-compose.prod.yml ./docker/restore.sh ./backups/20260314_120000
#
# WARNING: This will overwrite current data! Make a backup first.
# ============================================================================

set -euo pipefail

BACKUP_PATH="${1:?Usage: $0 <backup-directory>}"
COMPOSE_FILE="${COMPOSE_FILE:-docker/docker-compose.prod.yml}"

GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

info()  { echo -e "${GREEN}[RESTORE]${NC} $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
error() { echo -e "${RED}[ERROR]${NC} $*" >&2; }

if [ ! -d "${BACKUP_PATH}" ]; then
    error "Backup directory not found: ${BACKUP_PATH}"
    exit 1
fi

echo -e "${RED}WARNING: This will overwrite current data!${NC}"
echo "Backup source: ${BACKUP_PATH}"
echo ""
read -rp "Are you sure you want to continue? (yes/no): " CONFIRM
if [ "${CONFIRM}" != "yes" ]; then
    echo "Aborted."
    exit 0
fi

# ---------- 1. Stop Application Services ------------------------------------
info "Stopping application services (keeping databases running)..."
docker compose -f "${COMPOSE_FILE}" stop api worker 2>/dev/null || true

# ---------- 2. Restore PostgreSQL -------------------------------------------
if [ -f "${BACKUP_PATH}/postgres_all.sql.gz" ]; then
    info "Restoring PostgreSQL..."
    gunzip -c "${BACKUP_PATH}/postgres_all.sql.gz" \
        | docker compose -f "${COMPOSE_FILE}" exec -T postgres \
            psql -U "${POSTGRES_USER:-assethub}" -d postgres \
            --single-transaction 2>/dev/null

    if [ $? -eq 0 ]; then
        info "PostgreSQL restore complete."
    else
        error "PostgreSQL restore encountered errors (non-fatal warnings are expected)."
    fi
else
    warn "No PostgreSQL backup found — skipping."
fi

# ---------- 3. Restore MinIO ------------------------------------------------
if [ -f "${BACKUP_PATH}/minio_data.tar.gz" ]; then
    info "Restoring MinIO objects..."
    docker compose -f "${COMPOSE_FILE}" exec -T minio \
        sh -c 'rm -rf /data/* && tar -xzf - -C /' \
        < "${BACKUP_PATH}/minio_data.tar.gz"

    if [ $? -eq 0 ]; then
        info "MinIO restore complete."
    else
        error "MinIO restore failed!"
    fi
else
    warn "No MinIO backup found — skipping."
fi

# ---------- 4. Restore Keycloak Realm ----------------------------------------
if [ -d "${BACKUP_PATH}/keycloak-export" ]; then
    info "To restore Keycloak realm:"
    echo "  1. Copy export files to keycloak import volume"
    echo "  2. Restart Keycloak with --import-realm"
    echo "  3. Or import via Keycloak admin console"
    warn "Automatic Keycloak restore is not supported — use admin console."
else
    warn "No Keycloak export found — skipping."
fi

# ---------- 5. Restart Services ---------------------------------------------
info "Restarting all services..."
docker compose -f "${COMPOSE_FILE}" up -d

info "Restore complete! Verify services are healthy:"
echo "  docker compose -f ${COMPOSE_FILE} ps"
echo "  docker compose -f ${COMPOSE_FILE} logs -f api"
