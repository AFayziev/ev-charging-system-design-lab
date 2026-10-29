\set ON_ERROR_STOP on
\getenv charging_password CHARGING_MESSAGING_PASSWORD
\getenv notification_password NOTIFICATION_MESSAGING_PASSWORD
\getenv search_password SEARCH_MESSAGING_PASSWORD
\getenv media_password MEDIA_MESSAGING_PASSWORD

SELECT 'CREATE ROLE charging_messaging LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'charging_messaging') \gexec

ALTER ROLE charging_messaging PASSWORD :'charging_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO charging_messaging;

SELECT 'CREATE ROLE notification_messaging LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'notification_messaging') \gexec

ALTER ROLE notification_messaging PASSWORD :'notification_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO notification_messaging;

SELECT 'CREATE ROLE search_messaging LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'search_messaging') \gexec

ALTER ROLE search_messaging PASSWORD :'search_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO search_messaging;

SELECT 'CREATE ROLE media_messaging LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT'
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'media_messaging') \gexec

ALTER ROLE media_messaging PASSWORD :'media_password';
GRANT CONNECT ON DATABASE :"DBNAME" TO media_messaging;

CREATE SCHEMA IF NOT EXISTS api_messages AUTHORIZATION platform_migrator;
CREATE SCHEMA IF NOT EXISTS charging_messages AUTHORIZATION platform_migrator;
CREATE SCHEMA IF NOT EXISTS notification_messages AUTHORIZATION platform_migrator;
CREATE SCHEMA IF NOT EXISTS search_messages AUTHORIZATION platform_migrator;
CREATE SCHEMA IF NOT EXISTS media_messages AUTHORIZATION platform_migrator;
GRANT USAGE ON SCHEMA api_messages TO platform_writer;
GRANT USAGE ON SCHEMA charging_messages TO charging_messaging;
GRANT USAGE ON SCHEMA notification_messages TO notification_messaging;
GRANT USAGE ON SCHEMA search_messages TO search_messaging;
GRANT USAGE ON SCHEMA media_messages TO media_messaging;

ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA api_messages
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO platform_writer;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA charging_messages
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO charging_messaging;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA notification_messages
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO notification_messaging;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA search_messages
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO search_messaging;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA media_messages
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO media_messaging;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA api_messages
    GRANT USAGE ON SEQUENCES TO platform_writer;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA charging_messages
    GRANT USAGE ON SEQUENCES TO charging_messaging;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA notification_messages
    GRANT USAGE ON SEQUENCES TO notification_messaging;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA search_messages
    GRANT USAGE ON SEQUENCES TO search_messaging;
ALTER DEFAULT PRIVILEGES FOR ROLE platform_migrator IN SCHEMA media_messages
    GRANT USAGE ON SEQUENCES TO media_messaging;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA api_messages TO platform_writer;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA charging_messages TO charging_messaging;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA api_messages TO platform_writer;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA charging_messages TO charging_messaging;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA notification_messages TO notification_messaging;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA notification_messages TO notification_messaging;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA search_messages TO search_messaging;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA search_messages TO search_messaging;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA media_messages TO media_messaging;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA media_messages TO media_messaging;
