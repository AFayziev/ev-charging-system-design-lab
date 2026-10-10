\set ON_ERROR_STOP on
\getenv writer_password PLATFORM_WRITE_PASSWORD
\getenv reader_password PLATFORM_READ_PASSWORD
\getenv migrator_password PLATFORM_MIGRATION_PASSWORD

SELECT 'CREATE ROLE platform_writer LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_writer') \gexec
SELECT 'CREATE ROLE platform_reader LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_reader') \gexec
SELECT 'CREATE ROLE platform_migrator LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'platform_migrator') \gexec

ALTER ROLE platform_writer PASSWORD :'writer_password';
ALTER ROLE platform_reader PASSWORD :'reader_password';
ALTER ROLE platform_migrator PASSWORD :'migrator_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO platform_writer, platform_reader, platform_migrator;

CREATE SCHEMA IF NOT EXISTS charging AUTHORIZATION platform_migrator;
GRANT USAGE ON SCHEMA charging TO platform_writer, platform_reader;

ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA charging
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO platform_writer;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA charging
    GRANT SELECT ON TABLES TO platform_reader;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA charging
    GRANT USAGE ON SEQUENCES TO platform_writer;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA charging TO platform_writer;
GRANT SELECT ON ALL TABLES IN SCHEMA charging TO platform_reader;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA charging TO platform_writer;

-- Migration history belongs to the schema owner, including after reprovisioning.
SELECT 'REVOKE ALL ON TABLE charging.ef_migrations_history FROM platform_writer, platform_reader'
WHERE to_regclass('charging.ef_migrations_history') IS NOT NULL \gexec

-- Audit history remains append-only after reprovisioning.
SELECT 'REVOKE UPDATE, DELETE ON TABLE charging.audit_entries FROM platform_writer'
WHERE to_regclass('charging.audit_entries') IS NOT NULL \gexec
