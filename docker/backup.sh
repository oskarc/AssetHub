#!/usr/bin/env bash
# ============================================================================
# AssetHub — Backup Script
# ============================================================================
# Creates timestamped backups of all persistent data:
#   1. PostgreSQL database (pg_dump)
#   2. MinIO object storage (mc mirror)
#   3. Keycloak realm export
#
# Usage:
#   ./docker/backup.sh                    # Backup to ./backups/
#   ./docker/backup.sh /mnt/backup        # Backup to custom directory
#   COMPOSE_FILE=docker/docker-compose.prod.yml ./docker/backup.sh
#
# Restore:
#   See ./docker/restore.sh
# ============================================================================

set -euo pipefail

BACKUP_DIR="${1:-./backups}"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_PATH="${BACKUP_DIR}/${TIMESTAMP}"
COMPOSE_FILE="${COMPOSE_FILE:-docker/docker-compose.prod.yml}"

# Colours for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

info()  { echo -e "${GREEN}[BACKUP]${NC} $*"; }
warn()  { echo -e "${YELLOW}[WARN]${NC} $*"; }
error() { echo -e "${RED}[ERROR]${NC} $*" >&2; }

# Ensure backup directory exists
mkdir -p "${BACKUP_PATH}"
info "Backup destination: ${BACKUP_PATH}"

# ---------- 1. PostgreSQL --------------------------------------------------
info "Backing up PostgreSQL..."
docker compose -f "${COMPOSE_FILE}" exec -T postgres \
    pg_dumpall -U "${POSTGRES_USER:-assethub}" --clean \
    | gzip > "${BACKUP_PATH}/postgres_all.sql.gz"

if [ $? -eq 0 ]; then
    info "PostgreSQL backup complete: postgres_all.sql.gz ($(du -sh "${BACKUP_PATH}/postgres_all.sql.gz" | cut -f1))"
else
    error "PostgreSQL backup failed!"
    exit 1
fi

# ---------- 2. MinIO -------------------------------------------------------
info "Backing up MinIO objects..."
# Use docker cp to get files from the minio data volume
docker compose -f "${COMPOSE_FILE}" exec -T minio \
    tar -czf - /data 2>/dev/null \
    > "${BACKUP_PATH}/minio_data.tar.gz"

if [ $? -eq 0 ]; then
    info "MinIO backup complete: minio_data.tar.gz ($(du -sh "${BACKUP_PATH}/minio_data.tar.gz" | cut -f1))"
else
    warn "MinIO backup failed — you may need to use 'mc mirror' for large datasets."
fi

# ---------- 3. Keycloak Realm Export ----------------------------------------
info "Exporting Keycloak realm..."
# Keycloak 26+ supports partial export via admin API
docker compose -f "${COMPOSE_FILE}" exec -T keycloak \
    /opt/keycloak/bin/kc.sh export \
    --dir /tmp/keycloak-export \
    --realm media 2>/dev/null || true

docker compose -f "${COMPOSE_FILE}" cp \
    keycloak:/tmp/keycloak-export "${BACKUP_PATH}/keycloak-export" 2>/dev/null

if [ -d "${BACKUP_PATH}/keycloak-export" ]; then
    info "Keycloak realm export complete."
else
    warn "Keycloak realm export failed — export manually via admin console."
fi

# ---------- 4. Metadata ----------------------------------------------------
cat > "${BACKUP_PATH}/backup-info.json" <<EOF
{
    "timestamp": "${TIMESTAMP}",
    "date": "$(date -Iseconds)",
    "compose_file": "${COMPOSE_FILE}",
    "hostname": "$(hostname)"
}
EOF

# ---------- Summary ---------------------------------------------------------
info "Backup complete!"
echo ""
echo "  Location: ${BACKUP_PATH}"
echo "  Contents:"
ls -lh "${BACKUP_PATH}/" | tail -n +2 | sed 's/^/    /'
echo ""
echo "  To restore: ./docker/restore.sh ${BACKUP_PATH}"
