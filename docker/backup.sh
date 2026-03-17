#!/usr/bin/env bash
# ============================================================================
# AssetHub Backup
# ============================================================================
#
# Creates a timestamped backup of all persistent data:
#   - PostgreSQL (all databases via pg_dumpall, gzipped)
#   - MinIO object storage (tar archive of /data volume)
#
# Keycloak data lives in PostgreSQL, so it is included in the database dump.
# The Keycloak realm can also be exported separately via the admin console
# (Realm settings > Action > Partial export) if you need a portable JSON file.
#
# Usage:
#   ./docker/backup.sh                          # -> ./backups/<timestamp>/
#   ./docker/backup.sh /mnt/nfs/assethub        # -> /mnt/nfs/assethub/<timestamp>/
#
# Environment:
#   COMPOSE_FILE   Compose file to use     (default: docker/docker-compose.prod.yml)
#   POSTGRES_USER  Database superuser name  (default: assethub)
#
# ============================================================================

set -euo pipefail

# -- Configuration -----------------------------------------------------------

BACKUP_ROOT="${1:-./backups}"
TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
BACKUP_DIR="${BACKUP_ROOT}/${TIMESTAMP}"
COMPOSE_FILE="${COMPOSE_FILE:-docker/docker-compose.prod.yml}"
POSTGRES_USER="${POSTGRES_USER:-assethub}"

# -- Helpers -----------------------------------------------------------------

GREEN='\033[0;32m'  YELLOW='\033[1;33m'  RED='\033[0;31m'  NC='\033[0m'
info()  { echo -e "${GREEN}[backup]${NC} $*"; }
warn()  { echo -e "${YELLOW}[backup]${NC} $*"; }
die()   { echo -e "${RED}[backup]${NC} $*" >&2; exit 1; }

compose() { docker compose -f "${COMPOSE_FILE}" "$@"; }

file_size() { du -sh "$1" 2>/dev/null | cut -f1; }

# -- Pre-flight checks ------------------------------------------------------

command -v docker >/dev/null 2>&1 || die "docker is not installed."
[ -f "${COMPOSE_FILE}" ]          || die "Compose file not found: ${COMPOSE_FILE}"

# Verify required containers are running
for svc in postgres minio; do
    compose ps --status running "${svc}" 2>/dev/null | grep -q "${svc}" \
        || die "Container '${svc}' is not running. Start it first:\n  docker compose -f ${COMPOSE_FILE} up -d ${svc}"
done

# -- Create backup directory -------------------------------------------------

mkdir -p "${BACKUP_DIR}"
info "Destination: ${BACKUP_DIR}"

# -- 1. PostgreSQL -----------------------------------------------------------

PG_FILE="${BACKUP_DIR}/postgres.sql.gz"
info "Dumping PostgreSQL (all databases)..."

compose exec -T postgres \
    pg_dumpall -U "${POSTGRES_USER}" --clean \
    | gzip > "${PG_FILE}"

[ -s "${PG_FILE}" ] || die "PostgreSQL dump is empty — something went wrong."
info "PostgreSQL done ($(file_size "${PG_FILE}"))"

# -- 2. MinIO ----------------------------------------------------------------

MINIO_FILE="${BACKUP_DIR}/minio.tar.gz"
info "Archiving MinIO /data volume..."

compose exec -T minio \
    tar -czf - -C / data \
    > "${MINIO_FILE}"

if [ -s "${MINIO_FILE}" ]; then
    info "MinIO done ($(file_size "${MINIO_FILE}"))"
else
    warn "MinIO archive is empty — the bucket may have no objects yet."
fi

# -- 3. Metadata -------------------------------------------------------------

cat > "${BACKUP_DIR}/metadata.json" <<JSON
{
  "timestamp": "${TIMESTAMP}",
  "iso_date": "$(date -Iseconds)",
  "hostname": "$(hostname)",
  "compose_file": "${COMPOSE_FILE}",
  "postgres_user": "${POSTGRES_USER}",
  "files": {
    "postgres": "postgres.sql.gz",
    "minio": "minio.tar.gz"
  }
}
JSON

# -- Summary -----------------------------------------------------------------

echo ""
info "Backup complete."
echo ""
echo "  ${BACKUP_DIR}/"
ls -lh "${BACKUP_DIR}/" | tail -n +2 | sed 's/^/    /'
echo ""
echo "  Restore with:  ./docker/restore.sh ${BACKUP_DIR}"
