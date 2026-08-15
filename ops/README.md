# Ops

## Server qurulumu (staging/produksiya)

1. Docker + Docker Compose quraşdır; repo-nu `/opt/qrcatalog`-a klonla.
2. `.env` yarat (commit OLUNMUR):

   ```
   POSTGRES_PASSWORD=<güclü parol>
   ```

3. Backend env-ləri `docker-compose.prod.yml`-dəki `web.environment`-ə (və ya Coolify/portainer
   secrets-ə) əlavə et:

   | Açar | İzah |
   |---|---|
   | `Bootstrap__AdminEmail` / `Bootstrap__AdminPassword` | ilk admin (idempotent) |
   | `Bootstrap__CompanyName` / `Bootstrap__CompanySlug` | ilk müəssisə |
   | `Qr__PublicBaseUrl` | **QR-ın içinə düşən domen — S2-01 qərarı. Etiket çapından ƏVVƏL yazılmalıdır!** |
   | `Storage__Provider` | `S3` (R2 üçün) |
   | `Storage__S3__ServiceUrl` / `AccessKey` / `SecretKey` / `Bucket` / `PublicBaseUrl` | R2 |
   | `Email__Smtp__Host` / `Port` / `User` / `Password` / `From` | müraciət bildirişləri |
   | `Public__DefaultCompanySlug` | /katalog hansı müəssisəni göstərir (tək müəssisədə lazım deyil) |

4. `docker compose up -d --build` — migration-lar startup-da özləri tətbiq olunur.
5. Caddy (TLS): host-da və ya ayrıca konteynerdə; nümunə:

   ```
   qr.sirket.az {
       reverse_proxy localhost:8080
   }
   ```

   Qeyd: Caddy arxasında cookie-lərin `Secure` olması üçün gələcəkdə ForwardedHeaders
   qoşulmalıdır (hazırda `SameAsRequest`).

## Backup

- `backup.sh` — pg_dump → gzip → R2, 30 gün saxlanma. Cron: `0 3 * * *`.
- `restore-test.sh` — **sınanmamış backup backup deyil**: son backup-ı ayrıca bazaya bərpa edib
  cədvəl sayını yoxlayır. Ayda bir işə salın.
- Hər ikisinə env lazımdır: `PG*` + `R2_ENDPOINT` + `R2_BUCKET` + AWS açarları
  (`aws` CLI quraşdırılmış olmalıdır).

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
