ARG KAFKA_IMAGE
FROM ${KAFKA_IMAGE}
COPY topics.txt provision.sh /provisioning/
ENTRYPOINT ["/bin/sh", "/provisioning/provision.sh"]
