#!/usr/bin/env bash
# ============================================================================
# AssetHub Restore
# ============================================================================
#
# Restores a backup created by backup.sh. The script:
#   1. Stops the API and Worker (keeps databases running)
#   2. Restores PostgreSQL from the dump (includes Keycloak data)
#   3. Restores MinIO objects from the tar archive
#   4. Restarts all services and waits for healthy status
#
# Usage:
#   ./docker/restore.sh ./backups/20260314_120000
#
# Environment:
#   COMPOSE_FILE   Compose file to use     (default: docker/docker-compose.prod.yml)
#   POSTGRES_USER  Database superuser name  (default: assethub)
#
# ============================================================================

set -euo pipefail

# -- Configuration -----------------------------------------------------------

BACKUP_DIR="${1:?Usage: $0 <backup-directory>}"
COMPOSE_FILE="${COMPOSE_FILE:-docker/docker-compose.prod.yml}"
POSTGRES_USER="${POSTGRES_USER:-assethub}"

# -- Helpers -----------------------------------------------------------------

GREEN='\033[0;32m'  YELLOW='\033[1;33m'  RED='\033[0;31m'  BOLD='\033[1m'  NC='\033[0m'
info()  { echo -e "${GREEN}[restore]${NC} $*"; return 0; }
warn()  { echo -e "${YELLOW}[restore]${NC} $*"; return 0; }
die()   { echo -e "${RED}[restore]${NC} $*" >&2; exit 1; } # NOSONAR — exit always terminates; explicit return would be unreachable

compose() { docker compose -f "${COMPOSE_FILE}" "$@"; return $?; }

# -- Pre-flight checks ------------------------------------------------------

command -v docker >/dev/null 2>&1 || die "docker is not installed."
[[ -f "${COMPOSE_FILE}" ]]          || die "Compose file not found: ${COMPOSE_FILE}"
[[ -d "${BACKUP_DIR}" ]]            || die "Backup directory not found: ${BACKUP_DIR}"

PG_FILE="${BACKUP_DIR}/postgres.sql.gz"
MINIO_FILE="${BACKUP_DIR}/minio.tar.gz"

# Check what's available to restore
HAS_PG=false;    [[ -f "${PG_FILE}" ]]    && HAS_PG=true
HAS_MINIO=false; [[ -f "${MINIO_FILE}" ]] && HAS_MINIO=true

if ! ${HAS_PG} && ! ${HAS_MINIO}; then
    die "No backup files found in ${BACKUP_DIR}.\n  Expected: postgres.sql.gz and/or minio.tar.gz"
fi

# Show metadata if available
if [[ -f "${BACKUP_DIR}/metadata.json" ]]; then
    echo ""
    echo -e "  ${BOLD}Backup metadata:${NC}"
    cat "${BACKUP_DIR}/metadata.json" | sed 's/^/    /'
    echo ""
fi

# Show what will be restored
echo -e "  ${BOLD}Will restore:${NC}"
${HAS_PG}    && echo "    - PostgreSQL  ($(du -sh "${PG_FILE}" | cut -f1))"
${HAS_MINIO} && echo "    - MinIO       ($(du -sh "${MINIO_FILE}" | cut -f1))"
echo ""

# -- Confirmation ------------------------------------------------------------

echo -e "  ${RED}${BOLD}This will overwrite all current data.${NC}"
echo ""
read -rp "  Type 'restore' to continue: " CONFIRM
echo ""

if [[ "${CONFIRM}" != "restore" ]]; then
    echo "  Aborted."
    exit 0
fi

# -- 1. Stop application services -------------------------------------------

info "Stopping API and Worker..."
compose stop api worker 2>/dev/null || true

# Ensure databases are running
for svc in postgres minio; do
    compose ps --status running "${svc}" 2>/dev/null | grep -q "${svc}" || {
        info "Starting ${svc}..."
        compose up -d "${svc}"
        sleep 3
    }
done

# -- 2. Restore PostgreSQL ---------------------------------------------------

if ${HAS_PG}; then
    info "Restoring PostgreSQL (this may take a while for large databases)..."

    # pg_dumpall with --clean generates DROP statements, so non-fatal errors
    # about missing objects on a fresh database are expected.
    gunzip -c "${PG_FILE}" \
        | compose exec -T postgres \
            psql -U "${POSTGRES_USER}" -d postgres \
            -v ON_ERROR_STOP=0 \
            --quiet \
        2>&1 | grep -i 'error' | grep -vi 'does not exist' || true

    info "PostgreSQL restore complete."
fi

# -- 3. Restore MinIO -------------------------------------------------------

if ${HAS_MINIO}; then
    info "Restoring MinIO objects..."

    # Extract into a temp location first, then swap — avoids leaving an empty
    # /data directory if the tar extraction fails.
    compose exec -T minio sh -c '
        set -e
        mkdir -p /tmp/restore
        tar -xzf - -C /tmp/restore
        rm -rf /data/*
        if [ -d /tmp/restore/data ]; then
            cp -a /tmp/restore/data/* /data/ 2>/dev/null || true
        fi
        rm -rf /tmp/restore
    ' < "${MINIO_FILE}"

    info "MinIO restore complete."
fi

# -- 4. Restart everything ---------------------------------------------------

info "Restarting all services..."
compose up -d

echo ""
info "Restore complete. Verify with:"
echo ""
echo "  docker compose -f ${COMPOSE_FILE} ps"
echo "  curl -sf http://localhost:7252/health/ready && echo 'OK'"
echo ""
