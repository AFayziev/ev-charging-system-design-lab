ARG RUSTFS_CLI_IMAGE
FROM ${RUSTFS_CLI_IMAGE}
COPY *.json *.xml provision.sh /provisioning/
ENTRYPOINT ["/bin/sh", "/provisioning/provision.sh"]
