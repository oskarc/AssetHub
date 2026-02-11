#!/bin/bash
# Creates a second database for Keycloak on the same Postgres instance.
# Mounted as an init script so it runs automatically on first container start.
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
    SELECT 'CREATE DATABASE keycloak'
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'keycloak')\gexec
    GRANT ALL PRIVILEGES ON DATABASE keycloak TO $POSTGRES_USER;
EOSQL
