# Məhsul şəkilləri — promptlar və yükləmə

Kataloqdaki 15 demo məhsulun şəkli yoxdur. Bu sənəd onları yaratmaq üçündür.

**Fayl adı SKU olmalıdır** — `SZ-BH-AG.png`. Yükləyici faylı SKU-ya görə tapır.
İkinci şəkil: `SZ-BH-AG-2.png` (nömrəsiz fayl əsas şəkil olur).

**Ölçü:** ən azı **1280 piksel en**, tərcihən **1536×1024**. Tətbiq 320/640/1280/1920
variantlarını özü yaradır, amma şəkli BÖYÜTMÜR — kiçik fayl kataloqda bulanıq qalır.

---

## 1. Ortaq üslub (STYLE)

Bütün 15 şəkil bir kataloqa aid görünməlidir. Hər promptun sonuna eyni STYLE bloku əlavə olunur —
əks halda biri studiya fonunda, biri bağda çıxır və kataloq yamaq-yamaq görünür.

```
STYLE: Professional e-commerce product photograph. Single product, centered, three-quarter
view from slightly above eye level. Clean sunlit outdoor terrace setting: smooth light warm-grey
concrete floor, softly blurred neutral background, no walls or furniture behind. Bright natural
daylight from the upper left, soft realistic contact shadow under the product. Sharp focus on the
whole product, generous empty margin around it. 4:3 landscape framing.
NO people, NO text, NO logos, NO watermarks, NO price tags, NO collage or multiple angles,
NO plants or props competing with the product.
```

---

## 2. Codex agentinə tapşırıq

Bunu bütöv kopyalayıb Codex-ə verin. Repo kökündə işləməlidir.

```
Bu repoda (QrCatalog) 15 demo məhsul üçün şəkil hazırlamalısan.

QAYNAQ: ops/sekil-promptlari.md — orada STYLE bloku və hər SKU üçün məhsul təsviri var.
Hər şəkil promptu = "<məhsul təsviri>. <STYLE bloku>".

ADDIMLAR:
1. `sekiller/` qovluğu yarat (repo kökündə, .gitignore-a əlavə et — şəkillər commit olunmur).
2. Hər 15 SKU üçün bir şəkil generasiya et. OpenAI Images API işlət:
   model `gpt-image-1`, ölçü `1536x1024`, key `OPENAI_API_KEY` mühit dəyişənindən.
   Faylı `sekiller/<SKU>.png` kimi yaz.
3. Hər faylı yoxla: həqiqi PNG/JPEG olmalı, eni >= 1280 piksel, ölçüsü > 100 KB.
   Uyğun olmayanı yenidən generasiya et (maksimum 2 cəhd).
4. Şəkildə mətn, adam, loqo, ya birdən çox məhsul görünürsə — promptu dəyişmədən
   yenidən generasiya et (model bəzən mətn əlavə edir).
5. Sonda qovluğun məzmununu SKU-larla tutuşdur: 15 faylın hamısı olmalıdır,
   çatmayanı sadala.

ETMƏ:
- Şəkilləri repoya commit etmə (yalnız .gitignore sətrini commit et).
- src/ altındaki heç bir kodu dəyişmə — bu tapşırıq yalnız şəkil hazırlamaqdır.
- Mövcud .env faylına toxunma.
- SERVERƏ HEÇ NƏ YÜKLƏMƏ və deploy etmə: nə scp, nə ssh, nə docker. Şəkilləri yalnız
  lokal `sekiller/` qovluğuna yaz — yükləməni ayrıca biz edəcəyik.

API açarı yoxdursa DAYAN və bunu bildir. Şəkilləri Pillow ilə "çəkmə" — düz rəngli
plasholder kataloqu real göstərmir, məqsəd məhz odur.
```

Şəkillər hazır olandan sonra yükləmə (server tərəfində, `/opt/qrcatalog`-da):

```bash
python3 ops/upload-images.py sekiller
```

Lokal maşındasınızsa əvvəlcə serverə köçürün:

```bash
scp -r sekiller root@62.84.179.39:/opt/qrcatalog/
```

---

## 3. Məhsul təsvirləri (hər birinin sonuna STYLE əlavə olunur)

| SKU | Prompt (məhsul hissəsi) |
|---|---|
| `SZ-BH-AG` | A white stackable plastic sun lounger with a ribbed seat and integrated wheels at one end, adjustable backrest raised to a reclined position, smooth matte white polypropylene. |
| `SZ-BH-AN` | An anthracite dark-grey stackable plastic sun lounger, ribbed seat surface, adjustable backrest raised, matte charcoal polypropylene finish. |
| `SZ-RV-TX` | A folding sun lounger made of oiled acacia hardwood slats with a five-position adjustable backrest and two small wooden wheels at the foot end, warm honey-brown wood grain visible. |
| `SZ-PL-TK` | A modern sun lounger with a slim silver anodised aluminium frame and beige textilene mesh fabric seat, low profile, black frame accents, mesh weave texture visible. |
| `SZ-LX-YS` | A premium sun lounger with a brushed aluminium frame and a thick cream-white quilted cushion with piped edges, small matching headrest pillow. |
| `SK-PR-3N` | A three-seat public park bench: black cast-iron ornate side frames and legs, seat and backrest made of five thick pine wood slats stained warm brown, bolt holes visible in the feet. |
| `SK-IP-AK` | A two-seat garden bench in solid acacia wood, slatted seat and vertical-slat backrest, rounded armrests, natural warm wood tone, simple clean joinery. |
| `SK-PK-DS` | An A-frame wooden picnic table set where the table and two bench seats form one single connected unit, light pine wood with visible grain, sturdy angled legs. |
| `MS-YM-80` | A round white plastic garden table, 80 cm diameter, textured tabletop with a small centre hole for a parasol closed with a plug, three tapered legs, matte polypropylene. |
| `MS-DB-150` | A rectangular outdoor dining table with a light grey HPL tabletop and slim brushed aluminium legs, clean minimal design, 150 by 90 cm proportions. |
| `MS-QT-BL` | A small folding balcony table in pine wood with a metal hook bracket for mounting on a railing, shown unfolded, compact square top. |
| `CT-BG-300` | A large round garden parasol, 3 metre canopy in light sand-beige polyester, silver aluminium pole with a visible tilt joint below the canopy, canopy fully open, no base. |
| `CT-DV-YD` | A half-round wall-mounted awning parasol in beige polyester, flat back edge against nothing, semicircular canopy open, slim aluminium ribs and wall bracket visible. |
| `ST-ST-AG` | A white stackable garden chair in matte polypropylene with a slatted backrest and slightly curved seat, tapered legs with grey plastic floor tips. |
| `ST-KF-RT` | A cafe terrace chair with a slim aluminium frame fully wrapped in cappuccino-beige synthetic rattan weave, curved backrest, no cushion, tight even weave texture. |

---

## 4. Nəyi yoxlamaq lazımdır

Yükləmədən sonra kataloqa baxın: https://katalog.qrlog.az/katalog

- 13 dərc olunmuş məhsulun hamısında şəkil var
- Şəkillər bir üslubdadır (fon, işıq, rakurs eyni)
- Heç birində mətn, adam, ya loqo yoxdur
- Telefonda kart şəkilləri kəsilməyib (kartlar kvadratdır, mərkəz qırpılır)

`SZ-LX-YS` qaralama, `MS-QT-BL` arxiv məhsuldur — onların şəkli public saytda görünməyəcək,
amma admin paneldə lazımdır.
