import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useCategories } from '../api/categories'
import {
  useCreateProduct,
  useDeleteImage,
  useProduct,
  useProductAction,
  useReorderImages,
  useReplaceSpecs,
  useSetImageAlt,
  useUpdateProduct,
  useUploadImages,
  type SpecItem,
} from '../api/products'
import { useCreateQrCode, useQrCodesForTarget, useSheetDownload } from '../api/qrcodes'
import { UNIT_STATUSES, useUnits } from '../api/units'
import PointMap from '../components/PointMap'

/* ── spesifikasiya redaktoru ───────────────────────── */

function SpecsEditor({ productId, initial }: { productId: string; initial: SpecItem[] }) {
  const [rows, setRows] = useState<SpecItem[]>(initial)
  const [dirty, setDirty] = useState(false)
  const replace = useReplaceSpecs()

  useEffect(() => {
    setRows(initial)
    setDirty(false)
  }, [initial])

  function update(index: number, patch: Partial<SpecItem>) {
    setRows(rows.map((r, i) => (i === index ? { ...r, ...patch } : r)))
    setDirty(true)
  }

  return (
    <section className="mt-8">
      <h2 className="text-base font-semibold">Texniki spesifikasiya</h2>
      <p className="mt-1 text-sm text-stone-500">Ölçü, material, çəki, tutum…</p>

      <div className="mt-3 space-y-2">
        {rows.map((row, i) => (
          <div key={i} className="flex gap-2">
            <input
              value={row.label}
              onChange={e => update(i, { label: e.target.value })}
              placeholder="Ad (Ölçü)"
              className="w-40 rounded border border-stone-300 px-3 py-1.5 text-sm"
            />
            <input
              value={row.value}
              onChange={e => update(i, { value: e.target.value })}
              placeholder="Dəyər (190×60 sm)"
              className="flex-1 rounded border border-stone-300 px-3 py-1.5 text-sm"
            />
            <button type="button"
              onClick={() => { setRows(rows.filter((_, j) => j !== i)); setDirty(true) }}
              className="rounded px-2 text-sm text-red-600 hover:bg-red-50">
              Sil
            </button>
          </div>
        ))}
      </div>

      <div className="mt-3 flex gap-2">
        <button type="button"
          onClick={() => { setRows([...rows, { label: '', value: '' }]); setDirty(true) }}
          className="rounded border border-stone-300 px-3 py-1.5 text-sm hover:bg-stone-50">
          + Sətir
        </button>
        {dirty && (
          <button type="button"
            disabled={replace.isPending}
            onClick={() =>
              replace.mutate(
                { id: productId, specs: rows.filter(r => r.label.trim() && r.value.trim()) },
                { onSuccess: () => setDirty(false) },
              )}
            className="rounded bg-emerald-800 px-4 py-1.5 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-60">
            {replace.isPending ? 'Saxlanılır…' : 'Spesifikasiyanı saxla'}
          </button>
        )}
      </div>
      {replace.isError && (
        <p role="alert" className="mt-2 rounded bg-red-50 px-3 py-2 text-sm text-red-800">
          {replace.error.message}
        </p>
      )}
    </section>
  )
}

/* ── Bu model harada quraşdırılıb ───────────────────
   Xəritə + siyahı: hansı parkda, hoteldə, neçə ədəd. Satışdan sonra zəng gələndə
   ("bizdəki skamyalardan biri sınıb") cavab burada bir baxışda görünür. */
function SitesPanel({ productId }: { productId: string }) {
  const units = useUnits({ productId })
  const [focused, setFocused] = useState<string | null>(null)

  const rows = units.data ?? []
  const placed = rows.filter(u => u.latitude !== null && u.longitude !== null)

  // Hər NÜSXƏ ayrı nöqtədir — "24 skamya var" deyil, "bu skamya buradadır"
  const points = placed.map(unit => ({
    id: unit.id,
    lat: unit.latitude!,
    lng: unit.longitude!,
    title: unit.code,
    lines: [
      unit.siteName ?? 'anbarda',
      UNIT_STATUSES.find(s => s.value === unit.status)?.label ?? unit.status,
      unit.installedOn ? `quraşdırılıb: ${unit.installedOn}` : '',
    ],
    color: UNIT_STATUSES.find(s => s.value === unit.status)?.color ?? '#57534e',
    radius: 6,
  }))

  // Obyekt üzrə yığım — siyahı qısa qalsın, nöqtələr isə fərdi
  const bySite = new Map<string, { name: string; count: number }>()
  rows.forEach(unit => {
    const key = unit.siteId ?? 'stock'
    const name = unit.siteName ?? 'Anbarda'
    bySite.set(key, { name, count: (bySite.get(key)?.count ?? 0) + 1 })
  })

  return (
    <section className="mt-8">
      <h2 className="text-base font-semibold">Hər nüsxə harada</h2>
      <p className="mt-1 text-sm text-stone-500">
        {rows.length === 0
          ? 'Bu modelin hələ qeydə alınmış nüsxəsi yoxdur.'
          : `${rows.length} vahid, ${bySite.size} yer. Xəritədə hər nöqtə bir nüsxədir.`}
      </p>

      {rows.length > 0 && (
        <>
          {points.length > 0 && (
            <div className="mt-3">
              <PointMap
                points={points}
                selectedId={focused}
                onSelect={setFocused}
                className="h-72 w-full rounded-lg border border-stone-200"
              />
            </div>
          )}
          <ul className="mt-3 divide-y divide-stone-100 rounded-lg border border-stone-200 bg-white">
            {[...bySite.entries()].map(([key, entry]) => (
              <li key={key} className="flex items-center gap-3 px-3 py-2 text-sm">
                <span className="font-medium">{entry.name}</span>
                <span className="ml-auto tabular-nums">{entry.count} vahid</span>
              </li>
            ))}
          </ul>
          <Link
            to={`/vahidler?model=${productId}`}
            className="mt-3 inline-block text-sm text-emerald-800 hover:underline"
          >
            Nüsxələri ayrı-ayrı idarə et →
          </Link>
        </>
      )}
    </section>
  )
}

/* ── QR kodlar ──────────────────────────────────────
   Məhsulun etiketi buradan görünür və çap olunur. Əvvəl belə deyildi: kodlar yalnız
   ayrı səhifədə idi və orada növ də göstərilmirdi, ona görə "bu modelin kodu hansıdır"
   sualına cavab ad axtarmaqla verilirdi. Etiket fiziki məhsula yapışdırılır — səhv kod
   yapışdırmağın geri dönüşü yoxdur. */
function QrPanel({ productId }: { productId: string }) {
  const codes = useQrCodesForTarget(productId)
  const create = useCreateQrCode()
  const sheet = useSheetDownload()

  const items = codes.data?.items ?? []
  const active = items.filter(qr => qr.status === 'Active')

  return (
    <section className="mt-8">
      <h2 className="text-base font-semibold">QR kodlar</h2>
      <p className="mt-1 text-sm text-stone-500">
        Bu modelin etiketləri. Kod bir dəfə çap olunandan sonra silinmir — lazım olsa
        dayandırılır və ya başqa modelə yönləndirilir.
      </p>

      {codes.isPending ? (
        <p className="mt-3 text-sm text-stone-400">Yüklənir…</p>
      ) : items.length === 0 ? (
        <p className="mt-3 text-sm text-stone-500">Hələ kod yaradılmayıb.</p>
      ) : (
        <ul className="mt-3 divide-y divide-stone-100 rounded-lg border border-stone-200 bg-white">
          {items.map(qr => (
            <li key={qr.id} className="flex items-center gap-3 px-3 py-2 text-sm">
              <span className="font-mono font-medium">{qr.humanCode}</span>
              {qr.status !== 'Active' && (
                <span className="rounded bg-stone-100 px-1.5 py-0.5 text-[11px] uppercase tracking-wide text-stone-500">
                  dayandırılıb
                </span>
              )}
              <span className="ml-auto flex items-center gap-2">
                <a
                  href={`/api/admin/qrcodes/${qr.id}/image.svg`}
                  target="_blank"
                  rel="noreferrer"
                  className="text-emerald-800 hover:underline"
                >
                  SVG
                </a>
                <span className="text-stone-300">·</span>
                <a
                  href={`/api/admin/qrcodes/${qr.id}/image.png`}
                  className="text-emerald-800 hover:underline"
                >
                  PNG
                </a>
              </span>
            </li>
          ))}
        </ul>
      )}

      <div className="mt-3 flex flex-wrap gap-2">
        <button
          type="button"
          disabled={create.isPending}
          onClick={() => create.mutate({ targetType: 'product', targetId: productId })}
          className="rounded border border-stone-300 px-4 py-2 text-sm hover:bg-stone-50 disabled:opacity-50"
        >
          {create.isPending ? 'Yaradılır…' : 'Yeni kod yarat'}
        </button>
        {active.length > 0 && (
          <button
            type="button"
            disabled={sheet.isPending}
            onClick={() => sheet.mutate(active.map(qr => qr.id))}
            className="rounded border border-stone-300 px-4 py-2 text-sm hover:bg-stone-50 disabled:opacity-50"
          >
            {sheet.isPending ? 'Hazırlanır…' : 'Etiket vərəqi (PDF)'}
          </button>
        )}
      </div>
      {create.isError && (
        <p className="mt-2 text-sm text-red-700">{(create.error as Error).message}</p>
      )}
    </section>
  )
}

/* ── şəkil paneli ──────────────────────────────────── */

function ImagesPanel({ productId }: { productId: string }) {
  const product = useProduct(productId)
  const upload = useUploadImages()
  const remove = useDeleteImage()
  const reorder = useReorderImages()
  const setAlt = useSetImageAlt()

  const images = product.data?.images ?? []

  function move(index: number, delta: number) {
    const target = index + delta
    if (target < 0 || target >= images.length) return
    const ids = images.map(i => i.id)
    ;[ids[index], ids[target]] = [ids[target], ids[index]]
    reorder.mutate({ id: productId, orderedIds: ids })
  }

  return (
    <section className="mt-8">
      <h2 className="text-base font-semibold">Şəkillər</h2>
      <p className="mt-1 text-sm text-stone-500">
        JPEG, PNG və ya WebP, maks 15 MB. Avtomatik 4 ölçüdə WebP-ə çevrilir.
      </p>

      <label className="mt-3 inline-block cursor-pointer rounded border border-stone-300 px-4 py-2 text-sm hover:bg-stone-50">
        {upload.isPending ? 'Yüklənir…' : 'Şəkil əlavə et'}
        <input
          type="file"
          multiple
          accept="image/jpeg,image/png,image/webp"
          className="hidden"
          disabled={upload.isPending}
          onChange={e => {
            if (e.target.files?.length) upload.mutate({ id: productId, files: e.target.files })
            e.target.value = ''
          }}
        />
      </label>

      {(upload.isError || remove.isError) && (
        <p role="alert" className="mt-2 rounded bg-red-50 px-3 py-2 text-sm text-red-800">
          {upload.error?.message ?? remove.error?.message}
        </p>
      )}

      <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {images.map((image, index) => (
          <div key={image.id} className="rounded-lg border border-stone-200 bg-white p-2">
            <img
              src={image.variants[0]?.url}
              alt={image.altText ?? ''}
              className="h-36 w-full rounded object-cover"
            />
            <input
              defaultValue={image.altText ?? ''}
              placeholder="Alt mətn (əlçatanlıq/SEO)"
              onBlur={e => {
                const value = e.target.value.trim() || null
                if (value !== image.altText)
                  setAlt.mutate({ id: productId, imageId: image.id, altText: value })
              }}
              className="mt-2 w-full rounded border border-stone-200 px-2 py-1 text-xs"
            />
            <div className="mt-2 flex items-center justify-between text-xs">
              <div className="flex gap-1">
                <button type="button" onClick={() => move(index, -1)} disabled={index === 0}
                  className="rounded border border-stone-200 px-2 py-0.5 disabled:opacity-30">←</button>
                <button type="button" onClick={() => move(index, 1)}
                  disabled={index === images.length - 1}
                  className="rounded border border-stone-200 px-2 py-0.5 disabled:opacity-30">→</button>
              </div>
              <button type="button"
                onClick={() => {
                  if (window.confirm('Şəkil silinsin?'))
                    remove.mutate({ id: productId, imageId: image.id })
                }}
                className="rounded px-2 py-0.5 text-red-600 hover:bg-red-50">
                Sil
              </button>
            </div>
          </div>
        ))}
      </div>
    </section>
  )
}

/* ── əsas səhifə ───────────────────────────────────── */

export default function ProductEdit() {
  const { id } = useParams<{ id: string }>()
  const isNew = id === 'yeni'
  const navigate = useNavigate()

  const categories = useCategories()
  const product = useProduct(isNew ? undefined : id)
  const create = useCreateProduct()
  const update = useUpdateProduct()
  const action = useProductAction()

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [sku, setSku] = useState('')

  useEffect(() => {
    if (product.data) {
      setName(product.data.name)
      setDescription(product.data.description ?? '')
      setCategoryId(product.data.categoryId)
      setSku(product.data.sku ?? '')
    }
  }, [product.data])

  const busy = create.isPending || update.isPending
  const error = create.error ?? update.error ?? action.error

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    const payload = { name, description: description || null, categoryId, sku: sku || null }
    if (isNew) {
      create.mutate(payload, {
        onSuccess: result => navigate(`/mehsullar/${(result as { id: string }).id}`, { replace: true }),
      })
    } else {
      update.mutate({ id: id!, ...payload })
    }
  }

  function runAction(kind: 'publish' | 'unpublish' | 'archive' | 'copy') {
    if (kind === 'archive' &&
        !window.confirm('Məhsul arxivə keçsin? Public səhifə "istehsal olunmur" göstərəcək.'))
      return
    action.mutate({ id: id!, action: kind }, {
      onSuccess: result => {
        if (kind === 'copy' && result)
          navigate(`/mehsullar/${(result as { id: string }).id}`)
      },
    })
  }

  return (
    <div className="max-w-3xl">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-lg font-semibold tracking-tight">
          {isNew ? 'Yeni məhsul' : (product.data?.name ?? 'Məhsul')}
        </h1>
        {!isNew && product.data && (
          <div className="flex flex-wrap gap-2">
            {product.data.status !== 'Published' && (
              <button type="button" onClick={() => runAction('publish')}
                className="rounded bg-emerald-800 px-3 py-1.5 text-sm font-medium text-white hover:bg-emerald-700">
                Dərc et
              </button>
            )}
            {product.data.status === 'Published' && (
              <button type="button" onClick={() => runAction('unpublish')}
                className="rounded border border-stone-300 px-3 py-1.5 text-sm hover:bg-stone-50">
                Dərcdən götür
              </button>
            )}
            <button type="button" onClick={() => runAction('copy')}
              className="rounded border border-stone-300 px-3 py-1.5 text-sm hover:bg-stone-50">
              Kopyala
            </button>
            {product.data.status !== 'Archived' && (
              <button type="button" onClick={() => runAction('archive')}
                className="rounded border border-red-200 px-3 py-1.5 text-sm text-red-700 hover:bg-red-50">
                Arxivə keçir
              </button>
            )}
          </div>
        )}
      </div>

      <form onSubmit={onSubmit} className="mt-6 space-y-4">
        <label className="block">
          <span className="text-sm font-medium text-stone-700">Ad</span>
          <input required value={name} onChange={e => setName(e.target.value)}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700" />
        </label>

        <div className="flex flex-wrap gap-4">
          <label className="block min-w-56 flex-1">
            <span className="text-sm font-medium text-stone-700">Kateqoriya</span>
            <select required value={categoryId} onChange={e => setCategoryId(e.target.value)}
              className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm">
              <option value="">— seçin —</option>
              {categories.data?.map(c => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </label>
          <label className="block w-44">
            <span className="text-sm font-medium text-stone-700">SKU (istəyə görə)</span>
            <input value={sku} onChange={e => setSku(e.target.value)}
              className="mt-1 w-full rounded border border-stone-300 px-3 py-2 font-mono text-sm" />
          </label>
        </div>

        <label className="block">
          <span className="text-sm font-medium text-stone-700">Təsvir</span>
          <textarea rows={4} value={description} onChange={e => setDescription(e.target.value)}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700" />
        </label>

        {error && (
          <p role="alert" className="rounded bg-red-50 px-3 py-2 text-sm text-red-800">
            {error.message}
          </p>
        )}

        <button type="submit" disabled={busy}
          className="rounded bg-emerald-800 px-5 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-60">
          {busy ? 'Saxlanılır…' : isNew ? 'Yarat' : 'Saxla'}
        </button>
        {!isNew && update.isSuccess && !update.isPending && (
          <span className="ml-3 text-sm text-emerald-800">Saxlanıldı ✓</span>
        )}
      </form>

      {!isNew && id && product.data && (
        <>
          <SpecsEditor productId={id} initial={product.data.specs} />
          <ImagesPanel productId={id} />
          <QrPanel productId={id} />
          <SitesPanel productId={id} />
        </>
      )}
    </div>
  )
}
