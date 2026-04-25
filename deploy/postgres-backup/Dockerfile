# syntax=docker/dockerfile:1.7
# Postgres → S3-compatible (Backblaze B2 / MinIO) backup sidecar.
# Daily cron + pg_dump | gzip | mc pipe.

FROM alpine:3.19

# MinIO mc client — používáme latest stable. Pro reproducibility zafixuj hash
# nebo konkrétní release-date až MinIO publikuje v archive/ adresáři
# (URLkou archive/mc.RELEASE.{date}, ale staré releases jsou postupně mazané).
RUN apk add --no-cache \
        postgresql16-client \
        ca-certificates \
        bash \
        curl \
        tzdata \
    && curl -fsSL "https://dl.min.io/client/mc/release/linux-amd64/mc" -o /usr/local/bin/mc \
    && chmod +x /usr/local/bin/mc \
    && mc --version | head -1

ENV TZ=Europe/Prague \
    BACKUP_RETENTION_DAYS=14

COPY backup.sh /usr/local/bin/backup.sh
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /usr/local/bin/backup.sh /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]
