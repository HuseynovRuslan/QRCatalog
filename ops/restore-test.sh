#!/usr/bin/env bash
# SINANMAMIŞ BACKUP BACKUP DEYİL. Bu skript son backup-ı ayrıca bazaya bərpa edib
# cədvəl sayını yoxlayır. Ayda bir dəfə əl ilə, ya cron ilə işə salın.
# Env: backup.sh ilə eyni + RESTORE_DB (default: qrcatalog_restore_test)
set -euo pipefail

RESTORE_DB="${RESTORE_DB:-qrcatalog_restore_test}"

LATEST="$(aws s3 ls "s3://${R2_BUCKET}/backups/" --endpoint-url "$R2_ENDPOINT" \
    | awk '{print $4}' | sort | tail -n 1)"
[[ -n "$LATEST" ]] || { echo "XƏTA: backup tapılmadı"; exit 1; }

echo "Bərpa olunur: $LATEST → $RESTORE_DB"
aws s3 cp "s3://${R2_BUCKET}/backups/${LATEST}" /tmp/restore-test.sql.gz \
    --endpoint-url "$R2_ENDPOINT"

dropdb --if-exists "$RESTORE_DB"
createdb "$RESTORE_DB"
gunzip -c /tmp/restore-test.sql.gz | psql -d "$RESTORE_DB" -q
rm -f /tmp/restore-test.sql.gz

TABLES="$(psql -d "$RESTORE_DB" -tAc \
    "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'")"
PRODUCTS="$(psql -d "$RESTORE_DB" -tAc 'SELECT count(*) FROM "Products"' 2>/dev/null || echo 0)"

dropdb "$RESTORE_DB"

if [[ "$TABLES" -lt 10 ]]; then
    echo "XƏTA: bərpada yalnız $TABLES cədvəl çıxdı — backup şübhəlidir!"
    exit 1
fi
echo "OK: $LATEST bərpa olundu ($TABLES cədvəl, $PRODUCTS məhsul)"
