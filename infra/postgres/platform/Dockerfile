ARG POSTGRES_IMAGE
FROM ${POSTGRES_IMAGE}
COPY *.sql /provisioning/
ENTRYPOINT ["psql", "-X", "-q", "-f", "/provisioning/bootstrap.sql"]
