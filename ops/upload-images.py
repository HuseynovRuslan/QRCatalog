#!/usr/bin/env python3
"""Məhsul şəkillərini toplu yükləyir — admin paneldə hər məhsulu ayrıca açmağa ehtiyac yoxdur.

İki rejim var:

1. AD ÜZRƏ (default) — fayl adı SKU olmalıdır: `SZ-BH-AG.png`. İkinci, üçüncü şəkil üçün
   sonuna nömrə: `SZ-BH-AG-2.jpg`. Nömrəsiz fayl əsas şəkil olur.

       python3 ops/upload-images.py sekiller

2. SIRA ÜZRƏ (`--order`) — fayl adlarına baxılmır. Fayllar yaranma vaxtına görə düzülür və
   ops/sekil-promptlari.md-dəki 1–15 siyahısına uyğunlaşdırılır. ChatGPT-dən yüklənən
   şəkillərin adı "ChatGPT Image …" olur; onları əl ilə adlandırmamaq üçündür.

       python3 ops/upload-images.py sekiller --order          # uyğunluğu göstərir, soruşur
       python3 ops/upload-images.py sekiller --order --yes    # soruşmadan yükləyir

   Şəkli yenidən çəkib əvəz etmisinizsə vaxt sırası pozulur — o halda ad üzrə rejimi işlədin.

Tətbiq özü hər şəkli 4 ölçüdə WebP-yə çevirir və EXIF-i silir — burada yalnız yükləmə var.
Uyğun gəlməyən fayllar sadalanır və ATLANMIŞ sayılır (səssizcə itmir).
"""
import argparse
import http.cookiejar
import importlib.util
import json
import mimetypes
import os
import re
import sys
import urllib.error
import urllib.request
import uuid

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SUFFIXES = {".png", ".jpg", ".jpeg", ".webp"}


def seed_sku_order():
    """Sıra rejimi üçün kanonik SKU sırası — MƏNBƏ demo-seed.py-dır.

    Siyahını burada təkrarlamaq iki fayl arasında səssiz uyğunsuzluq riski yaradır
    (biri dəyişir, digəri köhnə qalır), ona görə fayl adındaki tire səbəbindən
    adi import mümkün olmadığı halda importlib ilə yüklənir.
    """
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "demo-seed.py")
    spec = importlib.util.spec_from_file_location("demo_seed", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return [sku for _category, _name, sku, *_rest in module.PRODUCTS]


def read_env(path):
    values = {}
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip()
    return values


class Client:
    def __init__(self, base):
        self.base = base.rstrip("/")
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(self.jar))

    def _open(self, request):
        try:
            with self.opener.open(request, timeout=120) as response:
                raw = response.read().decode("utf-8", "replace")
                return response.status, (json.loads(raw) if raw.strip() else None)
        except urllib.error.HTTPError as error:
            raw = error.read().decode("utf-8", "replace")
            return error.code, raw[:300]

    def token(self):
        request = urllib.request.Request(self.base + "/api/auth/antiforgery")
        return self._open(request)[1]["token"]

    def get(self, path):
        return self._open(urllib.request.Request(self.base + path))[1]

    def post_json(self, path, body):
        request = urllib.request.Request(
            self.base + path, data=json.dumps(body).encode(), method="POST")
        request.add_header("Content-Type", "application/json")
        request.add_header("X-XSRF-TOKEN", self.token())
        return self._open(request)

    def post_file(self, path, file_path):
        """multipart/form-data — sahə adı `files`, endpoint çoxlu fayl qəbul edir."""
        boundary = f"----qrc{uuid.uuid4().hex}"
        name = os.path.basename(file_path)
        content_type = mimetypes.guess_type(name)[0] or "application/octet-stream"
        with open(file_path, "rb") as handle:
            payload = handle.read()

        body = b"".join([
            f"--{boundary}\r\n".encode(),
            f'Content-Disposition: form-data; name="files"; filename="{name}"\r\n'.encode(),
            f"Content-Type: {content_type}\r\n\r\n".encode(),
            payload,
            f"\r\n--{boundary}--\r\n".encode(),
        ])
        request = urllib.request.Request(self.base + path, data=body, method="POST")
        request.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
        request.add_header("X-XSRF-TOKEN", self.token())
        return self._open(request)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("folder", help="şəkillərin olduğu qovluq")
    parser.add_argument("--base", default=os.environ.get("DEMO_BASE", "https://katalog.qrlog.az"))
    parser.add_argument("--order", action="store_true",
                        help="fayl adlarına baxma, yaranma sırası ilə SKU-lara uyğunlaşdır")
    parser.add_argument("--yes", action="store_true", help="sıra rejimində təsdiq soruşma")
    args = parser.parse_args()

    files = sorted(
        entry for entry in os.listdir(args.folder)
        if os.path.splitext(entry)[1].lower() in SUFFIXES)
    if not files:
        raise SystemExit(f"{args.folder} içində şəkil tapılmadı ({', '.join(sorted(SUFFIXES))})")

    # Sıra rejimi: adlar deyil, faylın yaranma vaxtı əsasdır
    order_map = None
    if args.order:
        skus = seed_sku_order()
        files = sorted(files, key=lambda name: os.path.getmtime(os.path.join(args.folder, name)))
        if len(files) > len(skus):
            raise SystemExit(
                f"Qovluqda {len(files)} şəkil var, siyahıda isə {len(skus)} məhsul — "
                "artıq fayllar hansına aid olduğu bilinmir. Onları çıxarın ya ad rejimini işlədin.")
        order_map = dict(zip(files, skus))
        print("Sıra üzrə uyğunluq (vaxta görə):")
        for name, sku in order_map.items():
            print(f"  {name}  →  {sku}")
        if len(files) < len(skus):
            print(f"\nQEYD: {len(skus) - len(files)} məhsul şəkilsiz qalır: "
                  f"{', '.join(skus[len(files):])}")
        if not args.yes:
            if input("\nDavam edilsin? (b/x) ").strip().lower() not in {"b", "y", "bəli", "yes"}:
                raise SystemExit("Dayandırıldı.")

    env = read_env(os.path.join(REPO, ".env"))
    client = Client(args.base)
    status, _ = client.post_json("/api/auth/login", {
        "email": env["Bootstrap__AdminEmail"],
        "password": env["Bootstrap__AdminPassword"],
    })
    if status >= 400:
        raise SystemExit(f"Giriş alınmadı: HTTP {status}")

    listing = client.get("/api/admin/products?page=1&pageSize=500")
    items = listing["items"] if isinstance(listing, dict) and "items" in listing else listing
    by_sku = {(row.get("sku") or "").upper(): row for row in items if row.get("sku")}

    uploaded, skipped = 0, []
    for name in files:
        if order_map is not None:
            key = order_map[name]
        else:
            # SKU-nun ÖZÜ rəqəmlə bitə bilər (MS-BG-180, SL-AS-120), "ikinci şəkil"
            # qaydası isə sonda -2 gözləyir. Ona görə əvvəlcə tam ad yoxlanılır;
            # yalnız uyğun gəlməyəndə sondaki nömrə kəsilir (SZ-AK-KL-2 → SZ-AK-KL).
            stem = os.path.splitext(name)[0].upper()
            key = stem if stem in by_sku else re.sub(r"-\d+$", "", stem)
        product = by_sku.get(key)
        if product is None:
            skipped.append((name, f"SKU tapılmadı: {key}"))
            continue
        status, response = client.post_file(
            f"/api/admin/products/{product['id']}/images", os.path.join(args.folder, name))
        if status >= 400:
            skipped.append((name, f"HTTP {status}: {response}"))
            continue
        uploaded += 1
        print(f"  {name}  →  {product['name']}")

    print(f"\nYükləndi: {uploaded} / {len(files)}")
    if skipped:
        print("Atlandı:")
        for name, reason in skipped:
            print(f"  {name} — {reason}")
        print("\nMövcud SKU-lar:")
        for sku in sorted(by_sku):
            print(f"  {sku}")
        sys.exit(1)


if __name__ == "__main__":
    main()
