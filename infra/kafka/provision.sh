#!/bin/sh
# Creates each topic in topics.txt when missing and applies its retention.
# Partition counts of existing topics are never changed.
set -eu

: "${BOOTSTRAP_SERVER:?BOOTSTRAP_SERVER is required}"
bin=/opt/kafka/bin

while read -r name partitions retention; do
  case "$name" in
    '' | '#'*) continue ;;
  esac
  "$bin/kafka-topics.sh" --bootstrap-server "$BOOTSTRAP_SERVER" --create --if-not-exists \
    --topic "$name" --partitions "$partitions" --config "retention.ms=$retention"
  "$bin/kafka-configs.sh" --bootstrap-server "$BOOTSTRAP_SERVER" --alter \
    --entity-type topics --entity-name "$name" --add-config "retention.ms=$retention"
done < /provisioning/topics.txt
