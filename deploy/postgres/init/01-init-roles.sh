#!/bin/sh
set -eu

MIGRATOR_PASS="$(tr -d '\r\n' < /run/secrets/postgres_migrator_password)"
APP_PASS="$(tr -d '\r\n' < /run/secrets/postgres_app_password)"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v migrator_pass="$MIGRATOR_PASS" \
  -v app_pass="$APP_PASS" <<'EOSQL'
SELECT format('CREATE ROLE dickifier_migrator LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', :'migrator_pass')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'dickifier_migrator') \gexec

SELECT format('ALTER ROLE dickifier_migrator WITH PASSWORD %L', :'migrator_pass') \gexec

SELECT format('CREATE ROLE dickifier_app LOGIN PASSWORD %L NOSUPERUSER NOCREATEDB NOCREATEROLE', :'app_pass')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'dickifier_app') \gexec

SELECT format('ALTER ROLE dickifier_app WITH PASSWORD %L', :'app_pass') \gexec

GRANT CONNECT ON DATABASE dickifier TO dickifier_migrator, dickifier_app;
GRANT USAGE, CREATE ON SCHEMA public TO dickifier_migrator;
GRANT USAGE ON SCHEMA public TO dickifier_app;

ALTER DEFAULT PRIVILEGES FOR ROLE dickifier_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO dickifier_app;

ALTER DEFAULT PRIVILEGES FOR ROLE dickifier_migrator IN SCHEMA public
  GRANT USAGE, SELECT ON SEQUENCES TO dickifier_app;
EOSQL
