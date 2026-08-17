#!/usr/bin/env python3
"""Demo məzmunu — sistemi müdürə/müştəriyə göstərmək üçün.

Boş kataloqda heç kim təsəvvür edə bilmir: kateqoriya ağacı, məhsul kartları, QR kodlar,
skan qrafiki və müraciətlər dolu olmalıdır. Bu skript hamısını yaradır.

    python3 ops/demo-seed.py           # boş sistemə əlavə edir
    python3 ops/demo-seed.py --wipe    # bazanı sıfırlayıb yenidən qurur
    python3 ops/demo-seed.py --clean   # yalnız silir (real işə başlamaq üçün)

ŞƏKİLLƏR DAXİL DEYİL. Hazır şəkilləri SKU adı ilə (məs. SZ-BH-AG.png) bir qovluğa yığıb
`python3 ops/upload-images.py <qovluq>` ilə toplu yükləyin.

Skan tarixçəsi qəsdən SQL ilə yazılır: API-dən 120 skan göndərmək rate limit-ə düşər və
hamısı "bu gün" görünərdi — qrafik isə 30 günlük seriya göstərməlidir.
"""
import argparse
import http.cookiejar
import json
import os
import random
import subprocess
import time
import urllib.error
import urllib.request

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

CATEGORIES = [
    ("Şezlonqlar", "SZ", "Hovuz, çimərlik və terras üçün massiv taxta uzanacaqlar."),
    ("Skameykalar", "SK", "Park, bağ və həyət skamyaları — massiv taxtadan."),
    ("Masalar", "MS", "Bağ və piknik masaları, qatlanan modellər."),
    ("Stullar", "ST", "Taxta bağ və terras stulları."),
    ("Salıncaqlar", "SL", "Bağ salıncaqları və asma oturacaqlar."),
]

# (kateqoriya, ad, SKU, təsvir, spesifikasiyalar, hal)
# HAMISI TAXTADANDIR — istehsal profili budur. Material sətri hər məhsulda ağac növünü
# və emalını göstərir, çünki açıq havada məhsulun ömrünü məhz bu təyin edir.
PRODUCTS = [
    ("Şezlonqlar", "Akasiya şezlonq, klassik", "SZ-AK-KL",
     "Massiv akasiya taxtasından, 5 pilləli arxalıqla. Təbii yağla işlənib — su damcısı "
     "içinə keçmir, günəş altında çatlamır. Arxa təkərlər sayəsində bir nəfər onu tək "
     "yerdəyişdirir.",
     [("Ölçü", "200×65×35 sm"), ("Material", "Massiv akasiya (yağlanmış)"),
      ("Çəki", "13 kq"), ("Arxalıq", "5 pilləli"), ("Təkər", "Var, arxa cütlük"),
      ("Maksimum yük", "150 kq"), ("Zəmanət", "2 il")],
     "published"),
    ("Şezlonqlar", "Termo-şam şezlonq", "SZ-TS-AC",
     "Termik emaldan keçmiş şam: rütubəti udmur, formasını saxlayır, hovuz kənarında da "
     "uzun ömürlüdür. Açıq bal rəngi vaxt keçdikcə gümüşü tona keçir.",
     [("Ölçü", "195×62×34 sm"), ("Material", "Termo-şam (ThermoWood)"),
      ("Çəki", "11 kq"), ("Səth", "Cilalanmış + yağ"), ("Maksimum yük", "140 kq"),
      ("Zəmanət", "3 il")],
     "published"),
    ("Şezlonqlar", "Akasiya şezlonq, yastıqlı", "SZ-AK-YS",
     "Klassik modelin yastıqlı variantı. Yastıq örtüyü açılır və maşında yuyulur — "
     "hovuz kənarında krem və günəş yağı ləkəsi problem olmur.",
     [("Ölçü", "200×68×36 sm"), ("Material", "Massiv akasiya + akril yastıq"),
      ("Yastıq", "Sökülən, yuyula bilən"), ("Çəki", "16 kq"),
      ("Maksimum yük", "150 kq"), ("Zəmanət", "2 il")],
     "published"),
    ("Şezlonqlar", "İkilik şezlonq, kölgəlikli", "SZ-IK-KL",
     "İki nəfərlik geniş uzanacaq və üstündə taxta karkaslı kölgəlik. Kölgəlik parçası "
     "sökülür — qışda anbara yalnız karkas qalır.",
     [("Ölçü", "200×140×175 sm"), ("Material", "Massiv şam (antiseptik)"),
      ("Kölgəlik", "Parça, sökülən"), ("Çəki", "42 kq"), ("Tutum", "2 nəfər"),
      ("Zəmanət", "2 il")],
     "published"),

    ("Skameykalar", "Park skamyası, 3 nəfərlik", "SK-PR-3N",
     "Qalın massiv şam oturacaq və arxalıq, möhkəm taxta ayaqlar. Ayaqlarda deşiklər var — "
     "ictimai məkanda yerə bərkidilir.",
     [("Ölçü", "180×62×85 sm"), ("Material", "Massiv şam (antiseptik + lak)"),
      ("Çəki", "34 kq"), ("Tutum", "3 nəfər"), ("Bərkidilmə", "Yerə bolt ilə"),
      ("Zəmanət", "3 il")],
     "published"),
    ("Skameykalar", "Bağ skamyası «İpək»", "SK-IP-AK",
     "Akasiyadan arxalıqlı skamya. Bütün kənarlar yumrulanıb — paltar ilişmir, uşaq "
     "əlini cızmır.",
     [("Ölçü", "150×58×85 sm"), ("Material", "Massiv akasiya"), ("Çəki", "21 kq"),
      ("Tutum", "2-3 nəfər"), ("Zəmanət", "2 il")],
     "published"),
    ("Skameykalar", "Arxalıqsız skamya, 150 sm", "SK-AR-150",
     "Sadə skamya — masanın kənarına, çardağa, dəhlizə. Hər iki tərəfdən oturulur, "
     "lazım olanda masa altına girir.",
     [("Ölçü", "150×35×45 sm"), ("Material", "Massiv şam (antiseptik)"),
      ("Çəki", "14 kq"), ("Tutum", "3 nəfər"), ("Zəmanət", "2 il")],
     "published"),
    ("Skameykalar", "Sandıqlı skamya", "SK-SD-QT",
     "Oturacaq qapaq kimi qalxır: yastıq, alət, uşaq oyuncağı içində saxlanılır. Qapaq "
     "yumşaq mexanizmlə enir — barmaq sıxmır.",
     [("Ölçü", "120×55×85 sm"), ("Material", "Massiv şam (lak)"), ("Həcm", "190 litr"),
      ("Çəki", "26 kq"), ("Qapaq", "Yumşaq enən"), ("Zəmanət", "2 il")],
     "published"),

    ("Masalar", "Piknik dəsti — masa və 2 skamya", "MS-PK-DS",
     "Masa və skamyalar bir bütövdür — açıq havada dağılmır, küləkdə yerindən tərpənmir. "
     "Şam taxtası antiseptiklə hopdurulub.",
     [("Ölçü", "160×150×75 sm"), ("Material", "Massiv şam (antiseptik)"),
      ("Çəki", "44 kq"), ("Tutum", "6 nəfər"), ("Yığılma", "Yığılmır, bütöv"),
      ("Zəmanət", "2 il")],
     "published"),
    ("Masalar", "Bağ masası, 180×90 sm", "MS-BG-180",
     "Altı-səkkiz nəfərlik ailə masası. Ayaqları söküləndir — qapıdan keçir, daşınmada "
     "yer tutmur.",
     [("Ölçü", "180×90×75 sm"), ("Material", "Massiv şam (yağlanmış)"),
      ("Çəki", "38 kq"), ("Tutum", "6-8 nəfər"), ("Ayaqlar", "Sökülən"),
      ("Zəmanət", "2 il")],
     "published"),
    ("Masalar", "Qatlanan bistro masası, Ø70", "MS-QT-70",
     "Balkon və kiçik terras üçün qatlanan masa. Bu model istehsaldan çıxıb — əvəzinə "
     "Bağ masasına baxın.",
     [("Diametr", "70 sm"), ("Hündürlük", "72 sm"), ("Material", "Massiv akasiya"),
      ("Çəki", "9 kq"), ("Qatlanma", "Var")],
     "archived"),

    ("Stullar", "Qatlanan akasiya stulu", "ST-AK-QT",
     "Qatlanır və divara söykənir — payızda hamısı bir küncə yığılır. Oturacaq və "
     "arxalıq bədənə uyğun əyilib.",
     [("Ölçü", "45×55×88 sm"), ("Material", "Massiv akasiya (yağlanmış)"),
      ("Çəki", "5,5 kq"), ("Qatlanma", "Var"), ("Maksimum yük", "130 kq"),
      ("Zəmanət", "2 il")],
     "published"),
    ("Stullar", "Adirondack stulu", "ST-AD-KL",
     "Geniş qollu, arxaya yatan klassik bağ stulu. Qolluq stəkan üçün kifayət qədər "
     "genişdir.",
     [("Ölçü", "78×85×95 sm"), ("Material", "Massiv şam (rəngli lak)"),
      ("Çəki", "12 kq"), ("Maksimum yük", "140 kq"), ("Zəmanət", "2 il")],
     "draft"),

    ("Salıncaqlar", "Bağ salıncağı, 3 nəfərlik", "SL-BG-3N",
     "Taxta karkas və parça kölgəlik. Zəncir yerinə taxta asqı — küləkdə cırıldamır, "
     "əl sıxmır.",
     [("Ölçü", "200×120×180 sm"), ("Material", "Massiv şam (antiseptik)"),
      ("Tutum", "3 nəfər"), ("Kölgəlik", "Parça, sökülən"),
      ("Maksimum yük", "250 kq"), ("Zəmanət", "2 il")],
     "published"),
    ("Salıncaqlar", "Asma salıncaq, 120 sm", "SL-AS-120",
     "Ağac budağına, pergolaya, ya terras tavanına asılır. Kəndir və qarmaqlar dəstin "
     "içindədir.",
     [("Ölçü", "120×60 sm"), ("Material", "Massiv akasiya (yağlanmış)"),
      ("Çəki", "12 kq"), ("Asma", "Kəndir + qarmaq dəstdə"),
      ("Maksimum yük", "200 kq"), ("Zəmanət", "2 il")],
     "published"),
]

INQUIRIES = [
    ("Elçin Məmmədov", "+994 50 318 22 47",
     "Salam, 40 ədəd akasiya şezlonq lazımdır. Hovuz üçün. Qiymət və çatdırılma müddəti?",
     "SZ-AK-KL", "InProgress"),
    ("Nurlan Əliyev", "+994 55 604 71 09",
     "Park skamyası 12 ədəd. Yerə bərkidilmə xidmətini də edirsinizmi?",
     "SK-PR-3N", "Answered"),
    ("Gülnar Həsənova", "+994 70 227 45 63",
     "Kafe terrası üçün 24 qatlanan stul və 6 masa. Rəng seçimi var?",
     "ST-AK-QT", None),
    ("Rəşad Quliyev", "+994 51 883 90 12",
     "Bağ salıncağı kölgəliklə birlikdə nə qədərdir? Həyətə almaq istəyirik.",
     "SL-BG-3N", None),
]


class Client:
    """Antiforgery + cookie axını ilə API müştərisi (yalnız stdlib)."""

    def __init__(self, base):
        self.base = base.rstrip("/")
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(self.jar))

    def _request(self, method, path, body=None, token=None):
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(self.base + path, data=data, method=method)
        if data is not None:
            request.add_header("Content-Type", "application/json")
        if token:
            request.add_header("X-XSRF-TOKEN", token)
        try:
            with self.opener.open(request, timeout=60) as response:
                raw = response.read().decode("utf-8", "replace")
                return response.status, (json.loads(raw) if raw.strip() else None)
        except urllib.error.HTTPError as error:
            raw = error.read().decode("utf-8", "replace")
            raise SystemExit(f"XƏTA {method} {path} → HTTP {error.code}: {raw[:400]}")

    def antiforgery(self):
        return self._request("GET", "/api/auth/antiforgery")[1]["token"]

    def get(self, path):
        return self._request("GET", path)[1]

    def send(self, method, path, body=None):
        return self._request(method, path, body, token=self.antiforgery())[1]

    def login(self, email, password):
        self.send("POST", "/api/auth/login", {"email": email, "password": password})


def read_env(path):
    values = {}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip()
    return values


def compose(*args, capture=False):
    command = ["docker", "compose", *args]
    if capture:
        return subprocess.run(command, cwd=REPO, capture_output=True, text=True,
                              check=True).stdout
    subprocess.run(command, cwd=REPO, check=True)
    return None


def sql(statement):
    """Bazaya birbaşa SQL — konteynerin içindən, host-a psql lazım deyil."""
    result = subprocess.run(
        ["docker", "compose", "exec", "-T", "postgres",
         "psql", "-U", "postgres", "-d", "qrcatalog", "-tA", "-v", "ON_ERROR_STOP=1",
         "-c", statement],
        cwd=REPO, capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"SQL xətası: {result.stderr.strip()[:500]}")
    return result.stdout.strip()


def wait_healthy(client, attempts=40):
    for _ in range(attempts):
        try:
            with urllib.request.urlopen(client.base + "/health", timeout=5) as response:
                if response.read().decode().strip() == "Healthy":
                    return
        except Exception:
            pass
        time.sleep(3)
    raise SystemExit("Tətbiq ayağa qalxmadı — `docker compose logs web` yoxla.")


def wipe(client):
    """Bazanı və yüklənmiş şəkilləri sıfırlayır; migration və bootstrap yenidən işləyir."""
    print("Baza sıfırlanır…")
    compose("stop", "web")
    compose("exec", "-T", "postgres", "dropdb", "-U", "postgres", "--if-exists", "qrcatalog")
    compose("exec", "-T", "postgres", "createdb", "-U", "postgres", "qrcatalog")
    volume = f"{os.path.basename(REPO)}_uploads"
    subprocess.run(["docker", "run", "--rm", "-v", f"{volume}:/data", "alpine:3",
                    "sh", "-c", "rm -rf /data/* /data/.[!.]* 2>/dev/null; true"],
                   check=False, capture_output=True)
    compose("start", "web")
    wait_healthy(client)
    print("  baza boşdur, tətbiq işləyir")


def seed(client):
    print("Müəssisə parametrləri…")
    client.send("PUT", "/api/admin/settings", {
        "name": "Açıq Hava Mebeli",
        "phone": "+994 12 000 00 00",
        "whatsappNumber": "994120000000",
    })

    print("Kateqoriyalar…")
    category_ids = {}
    for name, prefix, description in CATEGORIES:
        created = client.send("POST", "/api/admin/categories", {
            "name": name, "parentId": None, "description": description, "codePrefix": prefix,
        })
        category_ids[name] = created["id"]
        print(f"  {prefix}  {name}")

    print("Məhsullar…")
    product_ids = {}
    for category, name, sku, description, specs, state in PRODUCTS:
        created = client.send("POST", "/api/admin/products", {
            "name": name, "description": description,
            "categoryId": category_ids[category], "sku": sku,
        })
        product_id = created["id"]
        product_ids[sku] = product_id
        client.send("PUT", f"/api/admin/products/{product_id}/specs",
                    {"specs": [{"label": label, "value": value} for label, value in specs]})
        if state == "published":
            client.send("POST", f"/api/admin/products/{product_id}/publish")
        elif state == "archived":
            client.send("POST", f"/api/admin/products/{product_id}/publish")
            client.send("POST", f"/api/admin/products/{product_id}/archive")
        marker = {"published": "dərc", "draft": "qaralama", "archived": "arxiv"}[state]
        print(f"  {sku:<12} {name}  [{marker}]")

    print("QR kodlar…")
    codes = []
    for category, name, sku, _description, _specs, state in PRODUCTS:
        if state != "published":
            continue
        code = client.send("POST", "/api/admin/qrcodes",
                           {"targetType": "product", "targetId": product_ids[sku]})
        codes.append(code["humanCode"])
    category_code = client.send("POST", "/api/admin/qrcodes",
                                {"targetType": "category",
                                 "targetId": category_ids["Şezlonqlar"]})
    codes.append(category_code["humanCode"])
    print(f"  {len(codes)} kod: {', '.join(codes[:6])}…")

    print("Müraciətlər…")
    slugs = {}
    for sku, product_id in product_ids.items():
        slugs[sku] = client.get(f"/api/admin/products/{product_id}")["slug"]
    for name, phone, message, sku, status in INQUIRIES:
        client.send("POST", "/api/public/inquiries", {
            "name": name, "phone": phone, "message": message,
            "productSlug": slugs[sku], "qrToken": None, "website": None,
        })
        time.sleep(2)  # public forma dəqiqədə 5 sorğu qəbul edir — 4 müraciət sərhəddə sığır
    if any(status for *_rest, status in INQUIRIES):
        rows = client.get("/api/admin/inquiries")
        items = rows["items"] if isinstance(rows, dict) and "items" in rows else rows
        by_name = {row["name"]: row["id"] for row in items}
        for name, _phone, _message, _sku, status in INQUIRIES:
            if status and name in by_name:
                client.send("PUT", f"/api/admin/inquiries/{by_name[name]}/status",
                            {"status": status})
    print(f"  {len(INQUIRIES)} müraciət")

    print("Skan tarixçəsi (30 gün)…")
    # Sayları qəsdən qeyri-bərabər: bəzi model çox skan olunur, iki kod isə heç —
    # panel «heç vaxt skan olunmayıb» xəbərdarlığını da göstərməlidir.
    sql("""
        WITH ranked AS (
            SELECT q."Id", q."CompanyId",
                   -- Sıralama QƏSDƏN prefiksə görədir: əlifba sırası şezlonqları (SZ) sona
                   -- atır və panel əsas məhsul xəttini «ən az skan olunan» kimi göstərərdi.
                   row_number() OVER (ORDER BY
                       CASE left(q."HumanCode", 2)
                           WHEN 'SZ' THEN 1 WHEN 'SK' THEN 2 WHEN 'MS' THEN 3
                           WHEN 'SL' THEN 4 ELSE 5 END,
                       q."HumanCode") AS rn
            FROM "QrCodes" q
        ), counts AS (
            SELECT r.*, (ARRAY[41,28,23,17,14,11,9,7,6,5,4,3,2,0,0,0])[LEAST(r.rn, 16)] AS n
            FROM ranked r
        )
        INSERT INTO "ScanEvents" ("CompanyId", "QrCodeId", "OccurredAtUtc", "DeviceKind", "Lang")
        SELECT c."CompanyId", c."Id",
               -- kvadrat paylanma son günlərə doğru sıxlaşdırır
               now() - (power(random(), 2) * interval '29 days')
                     - (random() * interval '13 hours'),
               (ARRAY['mobile','mobile','mobile','mobile','tablet','desktop'])[1 + floor(random() * 6)],
               (ARRAY['az','az','az','ru','en'])[1 + floor(random() * 5)]
        FROM counts c, generate_series(1, 45) g
        WHERE g <= c.n;
    """)
    total = sql('SELECT count(*) FROM "ScanEvents";')
    print(f"  {total} skan yazıldı")


def summary(client):
    dashboard = client.get("/api/admin/stats/dashboard")
    print("\n=== Panel ===")
    print(f"  məhsul: {dashboard['productsTotal']} "
          f"(dərc olunmuş {dashboard['productsPublished']})")
    print(f"  30 günlük skan: {dashboard['scans30d']}")
    print(f"  skan olunmayan kod: {dashboard['unscannedCodes']}")
    print(f"  yeni müraciət: {dashboard['newInquiries']}")
    print("  ən çox skan olunanlar:")
    for row in dashboard["topProducts"]:
        print(f"    {row['count']:>4}  {row['name']}")
    print("\nŞəkilləri SKU adı ilə saxlayıb toplu yükləyin:")
    print("  " + ", ".join(f"{sku}.png" for _c, _n, sku, *_r in PRODUCTS[:4]) + " …")
    print("  python3 ops/upload-images.py <qovluq>")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", default=os.environ.get("DEMO_BASE", "https://katalog.qrlog.az"))
    parser.add_argument("--wipe", action="store_true", help="əvvəlcə bazanı sıfırla")
    parser.add_argument("--clean", action="store_true", help="yalnız sil, məzmun yaratma")
    args = parser.parse_args()

    random.seed(20260817)
    env = read_env(os.path.join(REPO, ".env"))
    client = Client(args.base)

    if args.wipe or args.clean:
        wipe(client)
        if args.clean:
            print("Sistem boşdur — real iş üçün hazırdır.")
            return

    client.login(env["Bootstrap__AdminEmail"], env["Bootstrap__AdminPassword"])
    existing = client.get("/api/admin/products")
    items = existing["items"] if isinstance(existing, dict) and "items" in existing else existing
    if items:
        raise SystemExit(f"Sistemdə artıq {len(items)} məhsul var. Sıfırlamaq üçün: --wipe")

    seed(client)
    summary(client)


if __name__ == "__main__":
    main()
