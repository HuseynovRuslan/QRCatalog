#!/usr/bin/env bash
# Gecə backup: pg_dump → gzip → R2. Cron: 0 3 * * * /opt/qrcatalog/ops/backup.sh
# Tələb olunan env (məs. /etc/qrcatalog/backup.env-dən source ilə):
#   PGHOST PGPORT PGDATABASE PGUSER PGPASSWORD
#   R2_ENDPOINT R2_BUCKET AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY
set -euo pipefail

STAMP="$(date -u +%Y%m%d-%H%M%S)"
FILE="/tmp/qrcatalog-${STAMP}.sql.gz"
KEEP_DAYS="${KEEP_DAYS:-30}"

pg_dump --no-owner --no-privileges | gzip > "$FILE"

aws s3 cp "$FILE" "s3://${R2_BUCKET}/backups/qrcatalog-${STAMP}.sql.gz" \
    --endpoint-url "$R2_ENDPOINT"

rm -f "$FILE"

# Köhnələri təmizlə (R2 lifecycle qaydası da alternativdir)
CUTOFF="$(date -u -d "-${KEEP_DAYS} days" +%Y%m%d)"
aws s3 ls "s3://${R2_BUCKET}/backups/" --endpoint-url "$R2_ENDPOINT" \
    | awk '{print $4}' \
    | while read -r key; do
        day="$(echo "$key" | sed -n 's/qrcatalog-\([0-9]\{8\}\).*/\1/p')"
        if [[ -n "$day" && "$day" < "$CUTOFF" ]]; then
            aws s3 rm "s3://${R2_BUCKET}/backups/${key}" --endpoint-url "$R2_ENDPOINT"
        fi
    done

echo "OK: qrcatalog-${STAMP}.sql.gz"
