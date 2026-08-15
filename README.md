# QrCatalog

QR etiketli məhsul kataloqu — SaaS. Açıq hava mebeli satan şirkətlər üçün: məhsulun üstündəki QR
skan olunanda alıcı məhsul səhifəsini görür, admin tərəfdə kataloq idarə olunur.

Sənədlər (rəsmi mənbə):
- Funksionallıq: https://claude.ai/code/artifact/26502b3b-c8da-4651-9e45-4105f7380791
- Texnologiya: https://claude.ai/code/artifact/c05aae48-02cb-4506-a846-3094d861c4f8
- Tətbiq planı: https://claude.ai/code/artifact/9357c733-804c-4f05-96ad-74ac53b5998a

## Stek

- **Backend** — .NET 10, ASP.NET Core (Razor Pages public + REST API), EF Core 10 + Npgsql.
  Migration-lar startup-da özləri tətbiq olunur.
- **Admin** — React 19 + Vite + TypeScript (`admin/`), build çıxışı `wwwroot/admin`-ə düşür.
- **Baza** — PostgreSQL (lokalda 18, serverdə compose ilə 18-alpine).
- **Multi-tenant, fail-closed** — hər tenant-scoped entity `ITenantOwned` daşıyır; cari şirkət
  təyin olunmayıbsa sorğu boş qayıdır, default şirkət YOXDUR.

## Lokal inkişaf (Docker tələb olunmur)

```bash
# 1. Lokal PostgreSQL 18 işə düşməlidir (Windows xidməti: postgresql-x64-18).
#    Bağlantı: appsettings.Development.json → localhost:5432, qrcatalog bazası.

# 2. Backend
dotnet run --project src/QrCatalog.Web        # http://localhost:5079

# 3. Admin SPA (ayrı terminalda, istəyə görə)
cd admin && npm install && npm run dev        # http://localhost:5173/admin/

# 4. Public sayt CSS-i (dəyişdirəndə)
cd src/QrCatalog.Web && npm install && npm run build:css
```

- `/health` — baza daxil vəziyyət
- `/scalar` — API sənədi (yalnız Development)

## Test

```bash
dotnet test                # unit + inteqrasiya
cd tests/e2e && npm test   # Playwright (işləyən tətbiq tələb edir — bax ops/README.md)
```

İnteqrasiya testləri real Postgres tələb edir və bazanı iki yolla tapır:

| Rejim | Nə vaxt | Necə |
|---|---|---|
| `TEST_PG` | Docker yoxdursa (tövsiyə olunan lokal yol) | Mövcud Postgres serverinə bağlanır, hər test sinfi üçün ayrı baza yaradıb sonra silir |
| Testcontainers | CI, ya da `DOCKER_AVAILABLE=true` | `postgres:18-alpine` konteyneri qaldırır |

Heç biri yoxdursa testlər sakit keçilir — **bu təhlükəlidir**: ilk CI işəsalması məhz belə
gizlənmiş altı qüsur tapdı. Docker olmayan maşında mütləq `TEST_PG` işlət:

```bash
# Postgres xidmət kimi işləmirsə, ayrıca klaster qaldırmaq kifayətdir:
initdb -D /tmp/qrpg/data -U postgres --pwfile=<parol-faylı>
pg_ctl -D /tmp/qrpg/data -o "-p 55432" -l /tmp/qrpg/pg.log start

TEST_PG="Host=localhost;Port=55432;Username=postgres;Password=...;Database=postgres" dotnet test
```

İnteqrasiya testləri **ardıcıl** işləyir (`DisableTestParallelization`): hər sinif öz host-unu
qaldırır və paralel halda `WebApplicationFactory` entry-point tapıcısı toqquşur.

## Server, backup, env siyahısı

Bax: [ops/README.md](ops/README.md). Qısa: `docker compose up` (web + postgres),
TLS-i Caddy bitirir, backup `ops/backup.sh` (pg_dump → R2) + `ops/restore-test.sh`
(sınanmamış backup backup deyil). **Etiket çapından əvvəl `Qr__PublicBaseUrl`
(S2-01 domen qərarı) mütləq təyin olunmalıdır** — QR-ın içinə düşən ünvan budur.

## Təhlükəsizlik xülasəsi

- Cookie auth (HttpOnly, SameSite=Strict) + antiforgery X-XSRF-TOKEN; API-də redirect yox 401/403
- Multi-tenant fail-closed query filtri; IgnoreQueryFilters yalnız sənədləşdirilmiş public yollarda
- CSP `script-src 'self'` (inline skript yoxdur), nosniff, frame-ancestors 'none'
- Login rate limit + lockout; public formalar honeypot + IP limit
- Şəkil yükləmə: magic-byte yoxlanışı, EXIF/GPS metadata atılır
- Audit jurnalı: kim, nə vaxt, nəyi, hansı dəyərdən (interceptor — endpoint unutması mümkün deyil)
- Public cavab strukturlarında daxili sahələr mövcud deyil
