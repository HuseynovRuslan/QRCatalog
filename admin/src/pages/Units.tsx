import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import PointMap, { type MapPoint } from '../components/PointMap'
import { useProducts } from '../api/products'
import { useSites } from '../api/sites'
import {
  UNIT_STATUSES,
  useBulkCreateUnits,
  useDeleteUnit,
  useMoveUnit,
  useUnits,
  useUpdateUnit,
  type Unit,
  type UnitStatus,
} from '../api/units'

const STATUS_COLOR = new Map(UNIT_STATUSES.map(s => [s.value, s.color]))
const STATUS_LABEL = new Map(UNIT_STATUSES.map(s => [s.value, s.label]))

export default function Units() {
  // Obyekt və məhsul səhifələrindən gələn linklər süzgəci hazır açır
  const [params] = useSearchParams()
  const [productId, setProductId] = useState(params.get('model') ?? '')
  const [siteId, setSiteId] = useState(params.get('obyekt') ?? '')
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<string | null>(null)
  const [moving, setMoving] = useState<Unit | null>(null)
  const [adding, setAdding] = useState(false)

  const units = useUnits({ productId, siteId, status, search })
  const products = useProducts('', '', '', 1)
  const sites = useSites('')
  const bulk = useBulkCreateUnits()
  const move = useMoveUnit()
  const update = useUpdateUnit()
  const remove = useDeleteUnit()

  const rows = units.data ?? []
  const productOptions = products.data?.items ?? []
  const siteOptions = sites.data ?? []

  // Anbardaki nüsxənin mövqeyi olmur — xəritəyə yalnız koordinatı olanlar düşür
  const points = useMemo<MapPoint[]>(
    () =>
      rows
        .filter(unit => unit.latitude !== null && unit.longitude !== null)
        .map(unit => ({
          id: unit.id,
          lat: unit.latitude!,
          lng: unit.longitude!,
          title: unit.code,
          lines: [
            unit.productName,
            unit.siteName ?? 'anbarda',
            STATUS_LABEL.get(unit.status) ?? unit.status,
            unit.installedOn ? `quraşdırılıb: ${unit.installedOn}` : '',
          ],
          color: STATUS_COLOR.get(unit.status) ?? '#57534e',
          radius: 6,
        })),
    [rows],
  )

  const onMap = points.length
  const inStock = rows.filter(u => u.siteId === null).length

  return (
    <div className="admin-page units-page">
      <div className="page-heading flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">İnventar</h1>
          <p className="mt-1 text-sm text-stone-500">
            Hər avadanlıq ayrıca qeyddə: {rows.length} ədəd, xəritədə {onMap}, anbarda {inStock}.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setAdding(!adding)}
          className="page-primary-action rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700"
        >
          {adding ? 'Bağla' : 'İnventar əlavə et'}
        </button>
      </div>

      {adding && (
        <BulkForm
          products={productOptions}
          sites={siteOptions}
          pending={bulk.isPending}
          error={bulk.error as Error | null}
          onSubmit={input =>
            bulk.mutate(input, {
              onSuccess: () => setAdding(false),
            })
          }
        />
      )}

      <div className="admin-filters mt-4 flex flex-wrap gap-2">
        <select
          value={productId}
          onChange={e => setProductId(e.target.value)}
          className="rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
        >
          <option value="">Bütün modellər</option>
          {productOptions.map(product => (
            <option key={product.id} value={product.id}>
              {product.name}
            </option>
          ))}
        </select>
        <select
          value={siteId}
          onChange={e => setSiteId(e.target.value)}
          className="rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
        >
          <option value="">Bütün obyektlər</option>
          {siteOptions.map(site => (
            <option key={site.id} value={site.id}>
              {site.name}
            </option>
          ))}
        </select>
        <select
          value={status}
          onChange={e => setStatus(e.target.value)}
          className="rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
        >
          <option value="">Bütün statuslar</option>
          {UNIT_STATUSES.map(item => (
            <option key={item.value} value={item.value}>
              {item.label}
            </option>
          ))}
        </select>
        <input
          value={search}
          onChange={e => setSearch(e.target.value)}
          placeholder="Kod üzrə axtar"
          className="w-48 rounded border border-stone-300 px-3 py-2 font-mono text-sm uppercase outline-none focus:border-emerald-700"
        />
      </div>

      <div className="mt-4">
        <PointMap
          points={points}
          selectedId={selected}
          onSelect={setSelected}
          onPick={
            moving
              ? (lat, lng) =>
                  move.mutate(
                    { id: moving.id, latitude: lat, longitude: lng },
                    { onSuccess: () => setMoving(null) },
                  )
              : undefined
          }
        />
        {moving ? (
          <p className="mt-2 text-sm text-emerald-800">
            <strong>{moving.code}</strong> üçün yeni yeri xəritədə klikləyin.{' '}
            <button
              type="button"
              onClick={() => setMoving(null)}
              className="underline hover:no-underline"
            >
              ləğv et
            </button>
          </p>
        ) : (
          <div className="mt-2 flex flex-wrap gap-3 text-xs text-stone-500">
            {UNIT_STATUSES.map(item => (
              <span key={item.value} className="flex items-center gap-1.5">
                <span
                  className="inline-block h-2.5 w-2.5 rounded-full"
                  style={{ backgroundColor: item.color }}
                />
                {item.label}
              </span>
            ))}
          </div>
        )}
      </div>

      <div className="responsive-table units-table mt-4 overflow-x-auto rounded-lg border border-stone-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-stone-200 text-left text-xs uppercase tracking-wide text-stone-400">
              <th className="px-3 py-2">Kod</th>
              <th className="px-3 py-2">Model</th>
              <th className="px-3 py-2">Obyekt</th>
              <th className="px-3 py-2">Status</th>
              <th className="px-3 py-2">Quraşdırılıb</th>
              <th className="px-3 py-2"></th>
            </tr>
          </thead>
          <tbody>
            {rows.map(unit => (
              <tr
                key={unit.id}
                onClick={() => setSelected(unit.id)}
                className={`cursor-pointer border-b border-stone-100 last:border-0 hover:bg-stone-50 ${
                  selected === unit.id ? 'bg-emerald-50/60' : ''
                }`}
              >
                <td data-label="Kod" className="px-3 py-2 font-mono font-medium">{unit.code}</td>
                <td data-label="Model" className="px-3 py-2 text-stone-600">{unit.productName}</td>
                <td data-label="Obyekt" className="px-3 py-2 text-stone-600">
                  {unit.siteName ?? <span className="text-stone-400">anbarda</span>}
                  {unit.siteName && !unit.hasOwnPosition && (
                    <span className="block text-xs text-stone-400">
                      dəqiq yer verilməyib — obyektin nöqtəsi
                    </span>
                  )}
                </td>
                <td data-label="Status" className="px-3 py-2">
                  <select
                    value={unit.status}
                    onClick={event => event.stopPropagation()}
                    onChange={event =>
                      update.mutate({
                        id: unit.id,
                        siteId: unit.siteId,
                        latitude: unit.hasOwnPosition ? unit.latitude : null,
                        longitude: unit.hasOwnPosition ? unit.longitude : null,
                        installedOn: unit.installedOn,
                        status: event.target.value as UnitStatus,
                        note: unit.note,
                      })
                    }
                    className="rounded border border-stone-200 px-2 py-1 text-xs outline-none focus:border-emerald-700"
                  >
                    {UNIT_STATUSES.map(item => (
                      <option key={item.value} value={item.value}>
                        {item.label}
                      </option>
                    ))}
                  </select>
                </td>
                <td data-label="Quraşdırılıb" className="px-3 py-2 text-stone-600 tabular-nums">
                  {unit.installedOn ?? '—'}
                </td>
                <td data-label="Əməliyyatlar" className="mobile-actions px-3 py-2 text-right whitespace-nowrap">
                  <button
                    type="button"
                    onClick={event => {
                      event.stopPropagation()
                      setMoving(unit)
                    }}
                    className="rounded px-2 py-1 text-xs text-emerald-800 hover:bg-emerald-50"
                  >
                    Yerini dəyiş
                  </button>
                  <button
                    type="button"
                    onClick={event => {
                      event.stopPropagation()
                      if (confirm(`${unit.code} qeydi tamamilə silinsin?`)) remove.mutate(unit.id)
                    }}
                    className="rounded px-2 py-1 text-xs text-stone-500 hover:bg-stone-100"
                  >
                    Sil
                  </button>
                </td>
              </tr>
            ))}
            {rows.length === 0 && (
              <tr>
                <td data-empty="true" colSpan={6} className="px-3 py-6 text-center text-stone-400">
                  Qeyd yoxdur. «İnventar əlavə et» ilə başlayın.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}

/* Toplu yaratma: real işdə "bu parka 24 skamya qoyduq" deyilir, 24 dəfə forma
   doldurulmur. Nüsxələr obyektin ətrafına səpələnir, sonra əl ilə dəqiqləşdirilir. */
function BulkForm({
  products,
  sites,
  pending,
  error,
  onSubmit,
}: {
  products: { id: string; name: string }[]
  sites: { id: string; name: string }[]
  pending: boolean
  error: Error | null
  onSubmit: (input: {
    productId: string
    siteId: string | null
    quantity: number
    installedOn: string | null
    note: string | null
    spreadMeters?: number
  }) => void
}) {
  const [productId, setProductId] = useState('')
  const [siteId, setSiteId] = useState('')
  const [quantity, setQuantity] = useState(1)
  const [installedOn, setInstalledOn] = useState('')
  const [spread, setSpread] = useState(60)

  return (
    <form
      onSubmit={event => {
        event.preventDefault()
        if (!productId) return
        onSubmit({
          productId,
          siteId: siteId || null,
          quantity,
          installedOn: installedOn || null,
          note: null,
          spreadMeters: spread,
        })
      }}
      className="mobile-form-card unit-create-form mt-4 rounded-lg border border-stone-200 bg-white p-4"
    >
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <label className="block">
          <span className="text-sm font-medium text-stone-700">Model</span>
          <select
            value={productId}
            onChange={e => setProductId(e.target.value)}
            required
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
          >
            <option value="">— seç —</option>
            {products.map(product => (
              <option key={product.id} value={product.id}>
                {product.name}
              </option>
            ))}
          </select>
        </label>
        <label className="block">
          <span className="text-sm font-medium text-stone-700">Obyekt</span>
          <select
            value={siteId}
            onChange={e => setSiteId(e.target.value)}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
          >
            <option value="">Anbar (hələ quraşdırılmayıb)</option>
            {sites.map(site => (
              <option key={site.id} value={site.id}>
                {site.name}
              </option>
            ))}
          </select>
        </label>
        <label className="block">
          <span className="text-sm font-medium text-stone-700">Ədəd</span>
          <input
            type="number"
            min={1}
            max={500}
            value={quantity}
            onChange={e => setQuantity(Number(e.target.value))}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
          />
        </label>
        <label className="block">
          <span className="text-sm font-medium text-stone-700">Quraşdırma tarixi</span>
          <input
            type="date"
            value={installedOn}
            onChange={e => setInstalledOn(e.target.value)}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
          />
        </label>
        <label className="block">
          <span className="text-sm font-medium text-stone-700">Səpələnmə (metr)</span>
          <input
            type="number"
            min={5}
            max={2000}
            value={spread}
            onChange={e => setSpread(Number(e.target.value))}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
          />
          <span className="mt-1 block text-xs text-stone-400">
            Avadanlıq obyektin ətrafına bu radiusda paylanır, sonra dəqiqləşdirilir.
          </span>
        </label>
      </div>

      <button
        type="submit"
        disabled={pending}
        className="mt-4 rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-50"
      >
        {pending ? 'Yaradılır…' : 'Yarat'}
      </button>
      {error && <p className="mt-2 text-sm text-red-700">{error.message}</p>}
    </form>
  )
}
