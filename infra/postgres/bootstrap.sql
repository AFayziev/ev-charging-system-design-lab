\set ON_ERROR_STOP on

REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE, TEMPORARY ON DATABASE :"DBNAME" FROM PUBLIC;
CREATE EXTENSION IF NOT EXISTS btree_gist;

\ir bootstrap-gateway-auth.sql
\ir bootstrap-charging.sql
