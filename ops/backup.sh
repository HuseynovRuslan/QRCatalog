#!/usr/bin/env bash
# Gecə backup: baza (pg_dump) + şəkillər (uploads volume) → R2.
# Cron: 0 3 * * * /opt/qrcatalog/ops/backup.sh >> /var/log/qrcatalog-backup.log 2>&1
#
# Baza konteynerdədir və portu QƏSDƏN publish olunmur, ona görə pg_dump host-dan deyil,
# `docker compose exec` ilə İÇƏRİDƏN işləyir — host-a postgresql-client lazım deyil.
#
# Tələb olunan env (məs. /etc/qrcatalog/backup.env-dən source ilə):
#   R2_ENDPOINT R2_BUCKET AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY
# İstəyə görə:
#   COMPOSE_DIR (default /opt/qrcatalog) · KEEP_DAYS (default 30)
#   UPLOADS_VOLUME (default <qovluq adı>_uploads) · SKIP_UPLOADS=1
set -euo pipefail

COMPOSE_DIR="${COMPOSE_DIR:-/opt/qrcatalog}"
KEEP_DAYS="${KEEP_DAYS:-30}"
STAMP="$(date -u +%Y%m%d-%H%M%S)"
DB_FILE="/tmp/qrcatalog-${STAMP}.sql.gz"

cd "$COMPOSE_DIR"

# ── Baza ──────────────────────────────────────────────────────────────────────
docker compose exec -T postgres \
    pg_dump -U postgres -d qrcatalog --no-owner --no-privileges \
    | gzip > "$DB_FILE"

# Boş/qırıq dump-ı R2-ya göndərmək backup illüziyası yaradır — ölçünü yoxla
if [[ "$(stat -c%s "$DB_FILE")" -lt 1000 ]]; then
    echo "XƏTA: dump şübhəli kiçikdir ($(stat -c%s "$DB_FILE") bayt) — R2-ya göndərilmir" >&2
    rm -f "$DB_FILE"
    exit 1
fi

aws s3 cp "$DB_FILE" "s3://${R2_BUCKET}/backups/qrcatalog-${STAMP}.sql.gz" \
    --endpoint-url "$R2_ENDPOINT"
rm -f "$DB_FILE"

# ── Şəkillər ──────────────────────────────────────────────────────────────────
# Storage__Provider=Local olanda məhsul şəkilləri BAZADA DEYİL, volume-dadır.
# Yedəklənməsə server itəndə bütün şəkillər gedir və QR-lar boş səhifəyə aparır.
# Storage__Provider=S3 (R2) işlədilirsə bu hissə lazım deyil: SKIP_UPLOADS=1 qoy.
if [[ "${SKIP_UPLOADS:-0}" != "1" ]]; then
    UPLOADS_VOLUME="${UPLOADS_VOLUME:-$(basename "$COMPOSE_DIR")_uploads}"
    if docker volume inspect "$UPLOADS_VOLUME" >/dev/null 2>&1; then
        IMG_FILE="/tmp/qrcatalog-uploads-${STAMP}.tar.gz"
        docker run --rm -v "${UPLOADS_VOLUME}:/data:ro" alpine:3 \
            tar -czf - -C /data . > "$IMG_FILE"
        aws s3 cp "$IMG_FILE" "s3://${R2_BUCKET}/backups/qrcatalog-uploads-${STAMP}.tar.gz" \
            --endpoint-url "$R2_ENDPOINT"
        rm -f "$IMG_FILE"
    else
        echo "QEYD: '$UPLOADS_VOLUME' volume-u tapılmadı — şəkil backup-ı atlandı." >&2
    fi
fi

# ── Köhnələri təmizlə (R2 lifecycle qaydası da alternativdir) ─────────────────
CUTOFF="$(date -u -d "-${KEEP_DAYS} days" +%Y%m%d)"
aws s3 ls "s3://${R2_BUCKET}/backups/" --endpoint-url "$R2_ENDPOINT" \
    | awk '{print $4}' \
    | while read -r key; do
        day="$(echo "$key" | sed -n 's/^qrcatalog-\(uploads-\)\?\([0-9]\{8\}\).*/\2/p')"
        if [[ -n "$day" && "$day" < "$CUTOFF" ]]; then
            aws s3 rm "s3://${R2_BUCKET}/backups/${key}" --endpoint-url "$R2_ENDPOINT"
        fi
    done

echo "OK: qrcatalog-${STAMP}"
