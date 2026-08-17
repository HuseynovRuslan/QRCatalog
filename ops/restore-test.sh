#!/usr/bin/env bash
# SINANMAMIŞ BACKUP BACKUP DEYİL. Bu skript son backup-ı ayrıca bazaya bərpa edib
# cədvəl və məhsul sayını yoxlayır. Ayda bir dəfə əl ilə, ya cron ilə işə salın.
#
# Baza konteynerdə olduğu üçün bərpa da `docker compose exec` ilə içəridən gedir.
# Canlı baza TOXUNULMUR: ayrıca test bazası yaradılır və sonda silinir.
#
# Env: backup.sh ilə eyni + RESTORE_DB (default: qrcatalog_restore_test)
set -euo pipefail

COMPOSE_DIR="${COMPOSE_DIR:-/opt/qrcatalog}"
RESTORE_DB="${RESTORE_DB:-qrcatalog_restore_test}"

cd "$COMPOSE_DIR"

# Yalnız baza dump-ları (uploads arxivləri istisna) — sonuncusu götürülür
LATEST="$(aws s3 ls "s3://${R2_BUCKET}/backups/" --endpoint-url "$R2_ENDPOINT" \
    | awk '{print $4}' | grep -E '^qrcatalog-[0-9]{8}-[0-9]{6}\.sql\.gz$' | sort | tail -n 1)"
[[ -n "$LATEST" ]] || { echo "XƏTA: baza backup-ı tapılmadı"; exit 1; }

echo "Bərpa olunur: $LATEST → $RESTORE_DB"
aws s3 cp "s3://${R2_BUCKET}/backups/${LATEST}" /tmp/restore-test.sql.gz \
    --endpoint-url "$R2_ENDPOINT"

pg() { docker compose exec -T postgres "$@"; }

pg dropdb -U postgres --if-exists "$RESTORE_DB"
pg createdb -U postgres "$RESTORE_DB"

# ON_ERROR_STOP olmasa psql sətir-sətir xəta verib yenə 0 ilə çıxır — yarımçıq bərpa
# "OK" kimi görünərdi. Sınaq bunun əksini sübut etmək üçündür.
gunzip -c /tmp/restore-test.sql.gz \
    | pg psql -U postgres -d "$RESTORE_DB" -q -v ON_ERROR_STOP=1
rm -f /tmp/restore-test.sql.gz

TABLES="$(pg psql -U postgres -d "$RESTORE_DB" -tAc \
    "SELECT count(*) FROM information_schema.tables WHERE table_schema='public'" | tr -d '\r')"
# Biznes cədvəli AYRICA yoxlanılır: yalnız cədvəl sayına baxmaq kifayət deyil, çünki
# Identity cədvəlləri tək başına həddi keçir və sxem-yalnız dump "OK" görünərdi.
PRODUCTS="$(pg psql -U postgres -d "$RESTORE_DB" -tAc 'SELECT count(*) FROM "Products"' | tr -d '\r')"
CODES="$(pg psql -U postgres -d "$RESTORE_DB" -tAc 'SELECT count(*) FROM "QrCodes"' | tr -d '\r')"

pg dropdb -U postgres "$RESTORE_DB"

if [[ "$TABLES" -lt 10 ]]; then
    echo "XƏTA: bərpada yalnız $TABLES cədvəl çıxdı — backup şübhəlidir!"
    exit 1
fi
# Canlı sistemdə məhsul və QR kodu HƏMİŞƏ olur; sıfır çıxsa dump boşdur.
if [[ "$PRODUCTS" -eq 0 || "$CODES" -eq 0 ]]; then
    echo "XƏTA: bərpa olunmuş baza boşdur ($PRODUCTS məhsul, $CODES kod) — backup etibarsızdır!"
    exit 1
fi
echo "OK: $LATEST bərpa olundu ($TABLES cədvəl, $PRODUCTS məhsul, $CODES QR kod)"
