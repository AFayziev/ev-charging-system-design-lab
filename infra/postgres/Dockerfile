ARG POSTGRES_IMAGE
FROM ${POSTGRES_IMAGE}
COPY bootstrap-gateway-auth.sql /bootstrap-gateway-auth.sql
ENTRYPOINT ["psql", "-X", "-q", "-f", "/bootstrap-gateway-auth.sql"]
