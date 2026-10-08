\set ON_ERROR_STOP on
\getenv runtime_password GATEWAY_AUTH_PASSWORD
\getenv migration_password GATEWAY_AUTH_MIGRATION_PASSWORD

SELECT 'CREATE ROLE gateway_auth_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gateway_auth_runtime') \gexec
SELECT 'CREATE ROLE gateway_auth_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gateway_auth_migrator') \gexec

ALTER ROLE gateway_auth_runtime PASSWORD :'runtime_password';
ALTER ROLE gateway_auth_migrator PASSWORD :'migration_password';

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE TEMPORARY ON DATABASE evcharging FROM PUBLIC;
GRANT CONNECT ON DATABASE evcharging TO gateway_auth_runtime, gateway_auth_migrator;

CREATE SCHEMA IF NOT EXISTS gateway_auth AUTHORIZATION gateway_auth_migrator;
GRANT USAGE ON SCHEMA gateway_auth TO gateway_auth_runtime;
