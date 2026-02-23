#!/bin/bash
# Creates a second database for Keycloak on the same Postgres instance
# with a dedicated user for isolation (security best practice).
# Mounted as an init script so it runs automatically on first container start.
set -e

# Use environment variables for Keycloak DB credentials
# Falls back to app credentials if not set (backwards compatibility)
KC_USER="${KEYCLOAK_DB_USER:-$POSTGRES_USER}"
KC_PASS="${KEYCLOAK_DB_PASSWORD:-$POSTGRES_PASSWORD}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
    -- Create keycloak user if it doesn't exist
    DO \$\$
    BEGIN
        IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '$KC_USER') THEN
            CREATE USER $KC_USER WITH PASSWORD '$KC_PASS';
        END IF;
    END
    \$\$;

    -- Create keycloak database if it doesn't exist
    SELECT 'CREATE DATABASE keycloak OWNER $KC_USER'
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'keycloak')\gexec

    -- Grant privileges (in case database already existed)
    GRANT ALL PRIVILEGES ON DATABASE keycloak TO $KC_USER;
EOSQL
