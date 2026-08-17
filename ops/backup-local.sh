#!/usr/bin/env bash
# ARA HƏLL: baza + şəkillər server DİSKİNƏ yedəklənir (R2 açarları hazır olmayanda).
# Cron: 0 3 * * * /opt/qrcatalog/ops/backup-local.sh >> /var/log/qrcatalog-backup.log 2>&1
#
# BU BACKUP ZƏİFDİR və müvəqqəti olmalıdır: fayllar yedəklədiyi serverin üstündədir.
# Disk sınsa, server itsə, ya da kimsə səhv əmr yazsa — baza ilə birlikdə backup da gedir.
# R2 açarları hazır olan kimi `ops/backup.sh`-a keçin (kənar saxlanma + bərpa sınağı).
#
# Env (istəyə görə): COMPOSE_DIR (default /opt/qrcatalog) · KEEP_DAYS (default 7)
#                    BACKUP_DIR (default $COMPOSE_DIR/backups)
set -euo pipefail

COMPOSE_DIR="${COMPOSE_DIR:-/opt/qrcatalog}"
BACKUP_DIR="${BACKUP_DIR:-$COMPOSE_DIR/backups}"
KEEP_DAYS="${KEEP_DAYS:-7}"
STAMP="$(date -u +%Y%m%d-%H%M%S)"

cd "$COMPOSE_DIR"
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

DB_FILE="$BACKUP_DIR/qrcatalog-${STAMP}.sql.gz"
docker compose exec -T postgres \
    pg_dump -U postgres -d qrcatalog --no-owner --no-privileges \
    | gzip > "$DB_FILE"

# Boş dump backup illüziyasıdır — saxlamağa dəyməz
if [[ "$(stat -c%s "$DB_FILE")" -lt 1000 ]]; then
    echo "XƏTA: dump şübhəli kiçikdir ($(stat -c%s "$DB_FILE") bayt)" >&2
    rm -f "$DB_FILE"
    exit 1
fi

# Şəkillər bazada deyil (Storage__Provider=Local) — ayrıca arxiv
UPLOADS_VOLUME="${UPLOADS_VOLUME:-$(basename "$COMPOSE_DIR")_uploads}"
if docker volume inspect "$UPLOADS_VOLUME" >/dev/null 2>&1; then
    docker run --rm -v "${UPLOADS_VOLUME}:/data:ro" alpine:3 \
        tar -czf - -C /data . > "$BACKUP_DIR/qrcatalog-uploads-${STAMP}.tar.gz"
fi

find "$BACKUP_DIR" -name 'qrcatalog-*' -type f -mtime "+${KEEP_DAYS}" -delete

echo "OK: ${STAMP} ($(du -sh "$BACKUP_DIR" | cut -f1) cəmi)"
