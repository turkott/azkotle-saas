# syntax=docker/dockerfile:1.7
# Postgres → S3-compatible (Backblaze B2 / MinIO) backup sidecar.
# Daily cron + pg_dump | gzip | mc pipe.

FROM alpine:3.19

ARG MC_VERSION=RELEASE.2024-12-13T22-19-12Z

RUN apk add --no-cache \
        postgresql16-client \
        ca-certificates \
        bash \
        curl \
        tzdata \
    && curl -fsSL "https://dl.min.io/client/mc/release/linux-amd64/archive/mc.${MC_VERSION}" -o /usr/local/bin/mc \
    && chmod +x /usr/local/bin/mc

ENV TZ=Europe/Prague \
    BACKUP_RETENTION_DAYS=14

COPY backup.sh /usr/local/bin/backup.sh
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /usr/local/bin/backup.sh /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]
