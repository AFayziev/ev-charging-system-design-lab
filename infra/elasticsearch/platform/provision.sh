#!/bin/sh
# Upserts the read-only search_reader role and user. Safe to run repeatedly.
set -eu

: "${ELASTICSEARCH_URL:?ELASTICSEARCH_URL is required}"
: "${ELASTIC_PASSWORD:?ELASTIC_PASSWORD is required}"
: "${SEARCH_READER_PASSWORD:?SEARCH_READER_PASSWORD is required}"

put() {
  curl --silent --show-error --fail --connect-timeout 5 --max-time 30 -u "elastic:$ELASTIC_PASSWORD" \
    -X PUT -H 'Content-Type: application/json' "$ELASTICSEARCH_URL$1" --data-binary "$2" > /dev/null
}

put /_security/role/search_reader "$(cat /provisioning/search-reader-role.json)"
put /_security/user/search_reader \
  "{\"password\":\"$SEARCH_READER_PASSWORD\",\"roles\":[\"search_reader\"]}"
