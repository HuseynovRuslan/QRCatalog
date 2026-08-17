# Ops

## Server qurulumu (addım-addım)

Tələb:
- Ubuntu 22.04+, **2 vCPU / 4 GB RAM** (image serverdə build olunur: npm + dotnet publish
  1 GB-lıq maşında yaddaşa sığmır. Az RAM varsa 2 GB swap aç: `fallocate -l 2G /swapfile &&
  chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile`).
- Domenin **A qeydi** serverin IP-sinə baxmalıdır — Caddy sertifikatı yalnız bundan sonra alır.
- root ya da sudo icazəsi.

### 1. Docker

```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker "$USER"   # sonra sessiyadan çıx-gir
```

### 2. Kodu gətir

```bash
sudo mkdir -p /opt/qrcatalog && sudo chown "$USER" /opt/qrcatalog
git clone https://github.com/HuseynovRuslan/QRCatalog.git /opt/qrcatalog
cd /opt/qrcatalog
```

### 3. `.env` hazırla

```bash
cp .env.example .env
nano .env
```

Doldurulması MƏCBURİ olanlar: `POSTGRES_PASSWORD`, `Bootstrap__*` (ilk admin və müəssisə),
`Qr__PublicBaseUrl`.

> **`Qr__PublicBaseUrl` geri dönüşü olmayan qərardır** (`S2-01`). Bu ünvan QR-ın İÇİNƏ
> yazılır: etiket çap olunandan sonra domeni dəyişmək çap olunmuş bütün kodları ölü edir.
> Etiket çapına qədər dəqiqləşdirilməlidir. Sonda `/` olmamalıdır.

### 4. Qaldır

```bash
docker compose up -d --build      # ilk build ~5-10 dəqiqə
docker compose logs -f web        # "Now listening on" görünməlidir
curl -s localhost:8080/health     # → Healthy
```

Migration-lar startup-da özləri tətbiq olunur; ayrıca əmr lazım deyil.

### 5. TLS — Caddy (host-da)

```bash
sudo apt install -y caddy
sudo nano /etc/caddy/Caddyfile
```

```
qr.sirket.az {
    reverse_proxy 127.0.0.1:8080
}
```

```bash
sudo systemctl reload caddy
```

Caddy sertifikatı özü alır və `X-Forwarded-For` / `X-Forwarded-Proto` başlıqlarını özü qoyur;
tətbiq onları oxuyur (`Program.cs`, ForwardedHeaders) — beləliklə cookie-lər `Secure` olur,
HSTS işləyir və rate limit hər ziyarətçini ayrı sayır.

> Konteyner portu **qəsdən yalnız 127.0.0.1-ə** bağlıdır (`docker-compose.yml`). Bu,
> ForwardedHeaders-in təhlükəsizlik şərtidir: port birbaşa internetə açılsa kənar adam
> `X-Forwarded-For` uydurub rate limit-i aldadar. Caddy başqa maşındadırsa `WEB_BIND`-i
> dəyiş, amma portu firewall ilə yalnız o maşına aç.

### 6. Firewall

```bash
sudo ufw allow 22,80,443/tcp && sudo ufw enable
```

8080 QƏSDƏN açılmır — o, yalnız 127.0.0.1-ə bağlıdır və Caddy vasitəsilə xidmət olunur.

### 7. Deploy sonrası yoxlama (ATLAMA)

Bu beş addım əvvəllər sınmış şeyləri yoxlayır:

1. `https://<domen>/health` → `Healthy`, brauzerdə kilid işarəsi var.
2. `docker compose ps` → web `healthy` (sadəcə `running` deyil).
3. `/admin`-ə gir (Bootstrap admin ilə), parolu dəyiş.
4. Bir məhsul yarat, **şəkil yüklə**, sonra `docker compose up -d --build` işlət və
   şəkilin YERİNDƏ olduğunu yoxla → `uploads` volume işləyir.
5. Bir QR kod yarat, PDF-i yüklə və **QR-ın içindəki ünvana bax**: `https://<domen>/q/...`
   olmalıdır. `http://` ya da IP görsən DAYAN — `Qr__PublicBaseUrl` səhvdir və çap etmə.
6. Yenidən `/admin`-ə bax: yeniləmədən sonra sistemdən çıxarılmamış olmalısan →
   Data Protection açarları volume-da qalır.

### 8. Yenilənmə

```bash
cd /opt/qrcatalog && git pull && docker compose up -d --build
```

**Bunları ETMƏ:**
- `docker compose down -v` — `-v` bazanı və şəkilləri SİLİR.
- `.env`-də `POSTGRES_PASSWORD`-u sonradan dəyişmək — Postgres parolu yalnız ilk
  qurulumda mənimsəyir; tətbiq yeni parolla bağlanmağa çalışıb uğursuz olar.
  Dəyişmək lazımdırsa əvvəlcə bazada `ALTER USER postgres PASSWORD ...` işlət.
- Repo-nu başqa qovluğa klonlamaq — volume adları qovluq adından gəlir, tətbiq boş
  bazada açılar və köhnə məlumat "yoxa çıxmış" görünər.

## Env açarları

| Açar | Məcburi | İzah |
|---|---|---|
| `POSTGRES_PASSWORD` | ✔ | baza parolu; verilməsə compose işə düşmür |
| `Bootstrap__AdminEmail` / `AdminPassword` | ✔ | ilk admin (idempotent) |
| `Bootstrap__CompanyName` / `CompanySlug` | ✔ | ilk müəssisə |
| `Qr__PublicBaseUrl` | ✔ | **QR-ın içindəki domen — geri dönüşü yoxdur** |
| `Storage__Provider` | — | `Local` (default) ya `S3` |
| `Storage__S3__ServiceUrl` / `AccessKey` / `SecretKey` / `Bucket` / `PublicBaseUrl` | S3-də ✔ | Cloudflare R2 |
| `Email__Smtp__Host` / `Port` / `User` / `Password` / `From` | — | müraciət bildirişləri; verilməsə forma işləyir, e-poçt getmir (log-a yazılır) |
| `Public__DefaultCompanySlug` | — | çox müəssisədə `/katalog` hansını göstərir |
| `WEB_BIND` | — | default `127.0.0.1:8080` |

Boş `ConnectionStrings__DefaultConnection` fail-fast verir — tətbiq "işləyir amma bazasız"
vəziyyətinə düşmür. `Qr__PublicBaseUrl` verilməsə produksiyada tətbiq ÜMUMİYYƏTLƏ qalxmır:
səhv domenlə çap olunmuş etiketi geri qaytarmaq mümkün deyil, ona görə burada sükut yolverilməzdir.

**Səssiz uğursuzluqlar** (tətbiq qalxır, `/health` yaşıl olur, amma bir şey işləmir):

| Nə | Nəticə | Necə görünür |
|---|---|---|
| `Bootstrap__AdminEmail/Password` boş ya parol 8 simvoldan qısa | admin yaranmır, panelə girə bilmirsən | `docker compose logs web` içində xəta sətri |
| `Email__Smtp__*` boş | müraciət forması işləyir, e-poçt getmir | log-da "SMTP konfiqurasiya olunmayıb" |
| `Public__DefaultCompanySlug` (ikinci müəssisə yarandıqdan sonra) | `/katalog` 404 verir | səhifə açılmır |
| `Storage__Provider=S3` amma açarlar səhv | şəkil yükləmə xəta verir | admin paneldə yükləmə uğursuz |

## Mövcud reverse proxy olan serverdə deploy

62.84.179.39 belədir: 80/443 `attendanceqr-caddy-1` konteynerindədir və bütün `qrlog.az`
saytlarına xidmət edir. Host-a ayrıca Caddy quraşdırmaq OLMAZ.

1. Override-i işə sal — tətbiq host portu yerinə proxy-nin şəbəkəsinə qoşulur:

   ```bash
   cd /opt/qrcatalog && ln -sfn docker-compose.shared-caddy.yml docker-compose.override.yml
   docker compose up -d --build
   ```

2. Paylaşılan Caddyfile-a blok əlavə et. **Ardıcıllıq vacibdir** — səhv sintaksis bütün
   saytları dayandıra bilər:

   ```bash
   cp -a /opt/attendanceqr/Caddyfile /opt/attendanceqr/Caddyfile.bak.$(date -u +%Y%m%d-%H%M%S)
   # bloku catch-all `https:// {`-dan ƏVVƏL əlavə et:
   #   katalog.qrlog.az {
   #       encode zstd gzip
   #       reverse_proxy qrcatalog-web:8080
   #   }
   docker exec attendanceqr-caddy-1 caddy validate --adapter caddyfile --config /etc/caddy/Caddyfile
   docker exec attendanceqr-caddy-1 caddy reload   --adapter caddyfile --config /etc/caddy/Caddyfile
   ```

   `validate` uğursuz olsa nüsxəni geri qaytar və `reload` ETMƏ. `reload` konteyneri
   yenidən başlatmır — səhv halda köhnə konfiqurasiya işləməyə davam edir.

   `security_headers` QƏSDƏN import olunmur: tətbiq öz başlıqlarını (CSP daxil) özü qoyur
   və inteqrasiya testi onları yoxlayır.

   Ticarət: web konteyneri proxy-nin şəbəkəsindədir, yəni oradaki digər konteynerlər onu
   görür (və əksinə). Alternativ — host portu + gateway IP-yə proxy — daha kövrəkdir.

## Backup

```bash
sudo mkdir -p /etc/qrcatalog
sudo nano /etc/qrcatalog/backup.env     # R2_ENDPOINT R2_BUCKET AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY
sudo apt install -y awscli
sudo crontab -e
```

```
0 3 * * * set -a; . /etc/qrcatalog/backup.env; set +a; /opt/qrcatalog/ops/backup.sh >> /var/log/qrcatalog-backup.log 2>&1
0 4 1 * * set -a; . /etc/qrcatalog/backup.env; set +a; /opt/qrcatalog/ops/restore-test.sh >> /var/log/qrcatalog-backup.log 2>&1
```

**R2 açarları hələ yoxdursa** `ops/backup-local.sh` işlədilir: eyni yedəkləri server diskinə
yazır (7 gün saxlama). Bu ARA HƏLLDİR — backup qoruduğu serverin üstündədir, server itsə
o da itir. Açarlar gələn kimi `backup.sh`-a keçin.

- `backup.sh` — baza (`pg_dump` konteyner içindən) + **şəkillər** (`uploads` volume, çünki
  `Storage__Provider=Local` olanda şəkillər bazada deyil) → R2, 30 gün saxlanma.
  Şübhəli kiçik dump R2-ya göndərilmir. R2 işlədilirsə `SKIP_UPLOADS=1` qoy.
- `restore-test.sh` — **sınanmamış backup backup deyil**: son backup-ı ayrıca test bazasına
  bərpa edib cədvəl və məhsul sayını yoxlayır, sonra test bazasını silir. Canlı baza toxunulmur.
- Host-a `postgresql-client` lazım DEYİL — hər ikisi `docker compose exec` işlədir
  (host-daki köhnə `pg_dump` PostgreSQL 18 serverini dump etməkdən imtina edərdi).
- Cron sətrindəki `set -a; . /etc/qrcatalog/backup.env; set +a` MƏCBURİDİR: cron boş
  mühitlə işləyir və skript `set -u` altında dərhal dayanar.
- R2 üçün env-də bunlar da olmalıdır: `AWS_DEFAULT_REGION=auto` və
  `AWS_REQUEST_CHECKSUM_CALCULATION=when_required` (yeni aws-cli R2-nin qəbul etmədiyi
  checksum başlıqları göndərir).

### Bərpa (produksiya çökəndə)

```bash
cd /opt/qrcatalog
aws s3 cp s3://$R2_BUCKET/backups/<fayl>.sql.gz /tmp/b.sql.gz --endpoint-url "$R2_ENDPOINT"
docker compose stop web
docker compose exec -T postgres dropdb -U postgres qrcatalog
docker compose exec -T postgres createdb -U postgres qrcatalog
gunzip -c /tmp/b.sql.gz | docker compose exec -T postgres psql -U postgres -d qrcatalog -v ON_ERROR_STOP=1
# Şəkillər (Storage__Provider=Local olanda):
aws s3 cp s3://$R2_BUCKET/backups/<fayl>-uploads.tar.gz /tmp/u.tar.gz --endpoint-url "$R2_ENDPOINT"
docker run --rm -v qrcatalog_uploads:/data -v /tmp:/backup alpine:3 \
    sh -c 'tar -xzf /backup/u.tar.gz -C /data'
docker compose start web
```

## Monitorinq

- `/health` — baza daxil; 503 = problem. Uptime Kuma və s. ilə izlə (F2).
- Loglar: `docker compose logs -f web` (Serilog konsol formatı).

## E2E testlər

`tests/e2e` — Playwright. CI-da avtomatik işləyir; lokal işə salmaq üçün:

```bash
# Tətbiq işləyən halda (dotnet run + Postgres):
cd tests/e2e && npm install && npx playwright install chromium
BASE_URL=http://localhost:5079 npx playwright test
```
