#!/usr/bin/env python3
"""Məhsul şəkillərini toplu yükləyir — admin paneldə hər məhsulu ayrıca açmağa ehtiyac yoxdur.

    python3 ops/upload-images.py /root/sekiller

FAYL ADI SKU OLMALIDIR: `SZ-BH-AG.png` → həmin SKU-lu məhsula qoşulur.
İkinci, üçüncü şəkil üçün sonuna nömrə: `SZ-BH-AG-2.jpg`, `SZ-BH-AG-3.webp`.
Sıra fayl adına görədir, yəni ilk şəkil (nömrəsiz) əsas şəkil olur.

Tətbiq özü hər şəkli 4 ölçüdə WebP-yə çevirir və EXIF-i silir — burada yalnız yükləmə var.
Uyğun gəlməyən fayllar sadalanır və ATLANMIŞ sayılır (səssizcə itmir).
"""
import argparse
import http.cookiejar
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
    args = parser.parse_args()

    files = sorted(
        entry for entry in os.listdir(args.folder)
        if os.path.splitext(entry)[1].lower() in SUFFIXES)
    if not files:
        raise SystemExit(f"{args.folder} içində şəkil tapılmadı ({', '.join(sorted(SUFFIXES))})")

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
        stem = os.path.splitext(name)[0]
        key = re.sub(r"-\d+$", "", stem).upper()  # SZ-BH-AG-2 → SZ-BH-AG
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
