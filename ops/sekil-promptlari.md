# Məhsul şəkilləri — promptlar və yükləmə

Kataloqdaki 15 demo məhsulun şəkli yoxdur. Bu sənəd onları yaratmaq üçündür.
**Bütün məhsullar taxtadandır** — promptlar da ağac növünü və emalını göstərir.

**Ölçü:** ən azı **1280 piksel en**, tərcihən **1536×1024**. Tətbiq 320/640/1280/1920
variantlarını özü yaradır, amma şəkli BÖYÜTMÜR — kiçik fayl kataloqda bulanıq qalır.

**Adlandırma:** lazım deyil. Şəkilləri siyahı sırası ilə yükləyin, yükləyici sıra rejimində
özü uyğunlaşdırır (aşağıda). SKU adı ilə saxlamaq isteğe bağlıdır.

---

## 1. ChatGPT ilə — 10 + 5 dəstə (API açarı LAZIM DEYİL)

Abunəlik şəkil generasiyasını ChatGPT tətbiqində verir; API açarı ayrı ödənişli məhsuldur və
buna görə lazım deyil.

### Birinci dəstə (1–10)

```
Mən taxta açıq hava mebeli kataloqu üçün məhsul şəkilləri hazırlayıram.
Aşağıdaki 10 məhsulun HƏR BİRİ üçün AYRI şəkil generasiya et.

Hamısına eyni üslub tətbiq olunmalıdır — kataloq bir bütöv görünməlidir:

Professional e-commerce product photograph of SOLID WOOD outdoor furniture. Single product,
centered, three-quarter view from slightly above eye level. Clean sunlit outdoor terrace:
smooth light warm-grey concrete floor, softly blurred neutral background, no walls or other
furniture behind. Bright natural daylight from the upper left, soft realistic contact shadow
under the product. Visible natural wood grain and slat structure, honest matte wood finish.
Sharp focus across the whole product, generous empty margin around it. 4:3 landscape framing.
NO people, NO text, NO logos, NO watermarks, NO price tags, NO collage, NO multiple angles,
NO plastic or aluminium parts unless stated.

MƏHSULLAR:
1. A solid acacia wood sun lounger built from smooth honey-brown oiled slats, five-position backrest raised to a reclined angle, two small wooden wheels at the foot end.
2. A thermally modified pine (ThermoWood) sun lounger, pale caramel-honey sanded slats, low slim profile, backrest slightly raised, no wheels.
3. A solid acacia wood sun lounger with a thick cream-white quilted cushion laid over the slats and a small matching headrest pillow, wooden frame clearly visible at the sides.
4. A wide two-person wooden day-bed lounger with a matching wooden canopy frame above it holding a beige fabric canopy, pine wood with antiseptic finish.
5. A three-seat park bench made entirely of thick solid pine: slatted seat and backrest, chunky square wooden legs, warm brown lacquered finish, small bolt holes visible in the feet.
6. A two-seat acacia wood garden bench with a slatted backrest and armrests, all edges rounded, natural warm honey wood tone.
7. A simple backless wooden bench 150 cm long, five pine slats on two solid wooden leg panels, natural pine tone.
8. A wooden storage bench with a hinged seat lid, lid propped slightly open to reveal the empty storage compartment inside, lacquered pine.
9. An A-frame wooden picnic table set where the table and two bench seats form one single connected unit, natural pine with visible grain, sturdy angled legs.
10. A large rectangular solid pine garden dining table, thick oiled plank top, straight square legs, no chairs around it.
```

### İkinci dəstə (11–15)

```
Eyni üslubla davam et — indi qalan 5 məhsul. Hər biri üçün ayrı şəkil.

11. A round folding acacia wood bistro table, 70 cm diameter, shown fully unfolded, slatted round top, slim wooden legs.
12. A folding acacia wood garden chair shown open, slatted seat and slatted backrest, honey oiled finish.
13. An Adirondack style garden chair made of pine with a smooth coloured lacquer finish, wide flat armrests, deeply reclined slatted back.
14. A three-seat wooden garden swing bench hanging from its own solid wooden A-frame, with a beige fabric canopy on top, pine wood.
15. A hanging acacia wood bench swing, 120 cm wide, suspended by thick natural ropes with metal hooks, shown hanging freely.
```

Şəkil çıxdıqca yoxlayın: **mətn, adam, loqo, qiymət etiketi, ya ikinci məhsul olmamalıdır.**
Belə şəkil görsəniz "bunu yenidən çək, üzərində yazı olmasın" deyin.

---

## 2. Yükləmə

Şəkilləri **siyahı sırası ilə** bir qovluğa yükləyin, adını dəyişməyə ehtiyac yoxdur:

```bash
python3 ops/upload-images.py sekiller --order
```

Sıra rejimi faylları yaranma vaxtına görə düzüb 1–15 siyahısına uyğunlaşdırır və yükləməzdən
əvvəl uyğunluğu göstərib təsdiq istəyir. Lokal maşındasınızsa əvvəlcə serverə köçürün:

```bash
scp -r sekiller root@62.84.179.39:/opt/qrcatalog/
```

Şəkli yenidən çəkib əvəz etmisinizsə vaxt sırası pozulur — o halda faylı SKU adı ilə saxlayın
(`SZ-AK-KL.png`, ikinci şəkil üçün `SZ-AK-KL-2.png`) və `--order` olmadan işlədin.

---

## 3. Sıra və SKU cədvəli

| № | SKU | Məhsul |
|---|---|---|
| 1 | `SZ-AK-KL` | Akasiya şezlonq, klassik |
| 2 | `SZ-TS-AC` | Termo-şam şezlonq |
| 3 | `SZ-AK-YS` | Akasiya şezlonq, yastıqlı |
| 4 | `SZ-IK-KL` | İkilik şezlonq, kölgəlikli |
| 5 | `SK-PR-3N` | Park skamyası, 3 nəfərlik |
| 6 | `SK-IP-AK` | Bağ skamyası «İpək» |
| 7 | `SK-AR-150` | Arxalıqsız skamya, 150 sm |
| 8 | `SK-SD-QT` | Sandıqlı skamya |
| 9 | `MS-PK-DS` | Piknik dəsti — masa və 2 skamya |
| 10 | `MS-BG-180` | Bağ masası, 180×90 sm |
| 11 | `MS-QT-70` | Qatlanan bistro masası, Ø70 — **arxiv** |
| 12 | `ST-AK-QT` | Qatlanan akasiya stulu |
| 13 | `ST-AD-KL` | Adirondack stulu — **qaralama** |
| 14 | `SL-BG-3N` | Bağ salıncağı, 3 nəfərlik |
| 15 | `SL-AS-120` | Asma salıncaq, 120 sm |

Sıra `ops/demo-seed.py`-dakı məhsul sırası ilə eynidir — `--order` rejimi məhz onu oxuyur,
ona görə siyahını dəyişəndə ikisi birlikdə dəyişməlidir.

---

## 4. Yükləmədən sonra yoxlanılacaqlar

https://katalog.qrlog.az/katalog

- 13 dərc olunmuş məhsulun hamısında şəkil var
- Şəkillər bir üslubdadır: eyni fon, eyni işıq istiqaməti, eyni rakurs
- Ağac rəngləri bir-birinə yaxındır (biri qırmızı maun, biri ağardılmış çıxmayıb)
- Heç birində mətn, adam, loqo yoxdur
- Telefonda kart şəkilləri kəsilməyib (kartlar kvadratdır, mərkəz qırpılır)

`MS-QT-70` arxiv, `ST-AD-KL` qaralama məhsuldur — şəkilləri public saytda görünməyəcək,
amma admin paneldə lazımdır.

---

## 5. Codex/API yolu (yalnız API açarınız varsa)

ChatGPT abunəliyi API-yə şamil olunmur. Açarınız varsa Codex-ə bu tapşırığı verə bilərsiniz:

```
Bu repoda 15 taxta məhsul üçün şəkil hazırla. Promptlar: ops/sekil-promptlari.md,
1-ci bölmə (üslub bloku + 15 məhsul təsviri).

1. `sekiller/` qovluğu yarat (.gitignore-da var).
2. Hər məhsul üçün OpenAI Images API ilə şəkil çək: model `gpt-image-1`, ölçü `1536x1024`,
   açar `OPENAI_API_KEY`. Fayl adı: `sekiller/<SKU>.png` (SKU cədvəli 3-cü bölmədədir).
3. Hər faylı yoxla: həqiqi PNG, en >= 1280, ölçü > 100 KB. Uyğun olmayanı yenidən çək (maks 2 cəhd).
4. Şəkildə mətn, adam, loqo, ya birdən çox məhsul varsa yenidən çək.
5. Sonda 15 faylın hamısını yoxla, çatmayanı sadala.

ETMƏ: şəkilləri commit etmə · src/ altında kod dəyişmə · .env-ə toxunma ·
HEÇ BİR SERVERƏ qoşulma (nə ssh, nə scp, nə docker) — yükləməni ayrıca biz edirik.
Açar yoxdursa DAYAN, Pillow ilə plasholder çəkmə.
```
