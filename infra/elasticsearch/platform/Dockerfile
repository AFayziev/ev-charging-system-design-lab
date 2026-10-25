ARG ELASTICSEARCH_IMAGE
FROM ${ELASTICSEARCH_IMAGE}
COPY search-reader-role.json provision.sh /provisioning/
ENTRYPOINT ["/bin/sh", "/provisioning/provision.sh"]
