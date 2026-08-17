import { useMemo, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import PointMap, { type MapPoint } from '../components/PointMap'
import { useProducts } from '../api/products'
import {
  SITE_KINDS,
  useCreateSite,
  useDeleteSite,
  useSites,
  useUpdateSite,
  type SaveSite,
  type Site,
  type SiteKind,
} from '../api/sites'

const KIND_COLOR: Record<SiteKind, string> = {
  Park: '#15803d',
  Hotel: '#1d4ed8',
  Cafe: '#b45309',
  School: '#7e22ce',
  Residential: '#0f766e',
  Beach: '#0284c7',
  Other: '#57534e',
}

const EMPTY: SaveSite & { id?: string } = {
  name: '',
  kind: 'Park',
  latitude: 40.4093,
  longitude: 49.8671, // Bakı — yeni obyekt buradan başlayır, sonra xəritədən dəqiqləşdirilir
  address: null,
  contactName: null,
  contactPhone: null,
  note: null,
}

export default function Sites() {
  const [search, setSearch] = useState('')
  const [productFilter, setProductFilter] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [form, setForm] = useState<(SaveSite & { id?: string }) | null>(null)

  const sites = useSites(search, productFilter)
  const products = useProducts('', '', '', 1)
  const create = useCreateSite()
  const update = useUpdateSite()
  const remove = useDeleteSite()

  const productOptions = products.data?.items ?? []
  const rows = sites.data ?? []

  const points = useMemo<MapPoint[]>(
    () =>
      rows.map(site => ({
        id: site.id,
        lat: site.latitude,
        lng: site.longitude,
        title: site.name,
        lines: [
          site.address ?? '',
          ...site.items.map(item => `${item.quantity} × ${item.productName}`),
          site.items.length === 0 ? 'vahid qeydə alınmayıb' : '',
        ],
        color: KIND_COLOR[site.kind] ?? KIND_COLOR.Other,
      })),
    [rows],
  )

  const totals = {
    sites: rows.length,
    units: rows.reduce((sum, site) => sum + site.totalQuantity, 0),
  }

  function startCreate() {
    setForm({ ...EMPTY })
    setSelectedId(null)
  }

  function startEdit(site: Site) {
    setForm({
      id: site.id,
      name: site.name,
      kind: site.kind,
      latitude: site.latitude,
      longitude: site.longitude,
      address: site.address,
      contactName: site.contactName,
      contactPhone: site.contactPhone,
      note: site.note,
    })
    setSelectedId(site.id)
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (!form) return

    const payload: SaveSite = {
      name: form.name,
      kind: form.kind,
      latitude: form.latitude,
      longitude: form.longitude,
      address: form.address,
      contactName: form.contactName,
      contactPhone: form.contactPhone,
      note: form.note,
    }

    if (form.id) await update.mutateAsync({ id: form.id, ...payload })
    else await create.mutateAsync(payload)

    setForm(null)
  }

  const busy = create.isPending || update.isPending

  return (
    <div className="admin-page sites-page">
      <div className="page-heading flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Obyektlər</h1>
          <p className="mt-1 text-sm text-stone-500">
            Məhsulların quraşdırıldığı yerlər. {totals.sites} obyekt, {totals.units} vahid.
          </p>
        </div>
        <button
          type="button"
          onClick={startCreate}
          className="page-primary-action rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700"
        >
          Yeni obyekt
        </button>
      </div>

      <div className="admin-filters mt-4 flex flex-wrap gap-2">
        <input
          value={search}
          onChange={e => setSearch(e.target.value)}
          placeholder="Ad və ya ünvan üzrə axtar"
          className="w-full max-w-sm rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
        />
        {/* Model üzrə süzgəc: "bu model harada var" sualına xəritədə cavab verir */}
        <select
          value={productFilter}
          onChange={e => setProductFilter(e.target.value)}
          className="rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
        >
          <option value="">Bütün məhsullar</option>
          {productOptions.map(product => (
            <option key={product.id} value={product.id}>
              {product.name}
            </option>
          ))}
        </select>
      </div>

      <div className="mt-4">
        <PointMap
          points={points}
          selectedId={selectedId}
          onSelect={setSelectedId}
          onPick={
            form ? (lat, lng) => setForm({ ...form, latitude: lat, longitude: lng }) : undefined
          }
          draft={form ? { lat: form.latitude, lng: form.longitude } : null}
        />
        {form && (
          <p className="mt-2 text-sm text-emerald-800">
            Mövqeyi dəyişmək üçün xəritəyə klikləyin — qırmızı nöqtə seçilmiş yerdir.
          </p>
        )}
        <p className="mt-2 text-sm text-stone-500">
          Buradaki nöqtə obyektin özüdür. Ayrı-ayrı skamya və şezlonqların dəqiq yeri{' '}
          <Link to="/vahidler" className="text-emerald-800 hover:underline">
            Vahidlər
          </Link>{' '}
          səhifəsindədir.
        </p>
      </div>

      {form && (
        <form onSubmit={onSubmit} className="mobile-form-card mt-4 rounded-lg border border-stone-200 bg-white p-4">
          <h2 className="text-base font-semibold">
            {form.id ? 'Obyekti redaktə et' : 'Yeni obyekt'}
          </h2>

          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <label className="block">
              <span className="text-sm font-medium text-stone-700">Ad</span>
              <input
                value={form.name}
                onChange={e => setForm({ ...form, name: e.target.value })}
                required
                maxLength={200}
                placeholder="Dədə Qorqud parkı"
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
              />
            </label>

            <label className="block">
              <span className="text-sm font-medium text-stone-700">Növ</span>
              <select
                value={form.kind}
                onChange={e => setForm({ ...form, kind: e.target.value as SiteKind })}
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
              >
                {SITE_KINDS.map(kind => (
                  <option key={kind.value} value={kind.value}>
                    {kind.label}
                  </option>
                ))}
              </select>
            </label>

            <label className="block sm:col-span-2">
              <span className="text-sm font-medium text-stone-700">Ünvan</span>
              <input
                value={form.address ?? ''}
                onChange={e => setForm({ ...form, address: e.target.value || null })}
                maxLength={300}
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
              />
            </label>

            <label className="block">
              <span className="text-sm font-medium text-stone-700">Əlaqədar şəxs</span>
              <input
                value={form.contactName ?? ''}
                onChange={e => setForm({ ...form, contactName: e.target.value || null })}
                maxLength={200}
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
              />
            </label>

            <label className="block">
              <span className="text-sm font-medium text-stone-700">Telefon</span>
              <input
                value={form.contactPhone ?? ''}
                onChange={e => setForm({ ...form, contactPhone: e.target.value || null })}
                maxLength={40}
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
              />
            </label>

            <label className="block">
              <span className="text-sm font-medium text-stone-700">Enlik</span>
              <input
                type="number"
                step="0.000001"
                value={form.latitude}
                onChange={e => setForm({ ...form, latitude: Number(e.target.value) })}
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 font-mono text-sm outline-none focus:border-emerald-700"
              />
            </label>

            <label className="block">
              <span className="text-sm font-medium text-stone-700">Uzunluq</span>
              <input
                type="number"
                step="0.000001"
                value={form.longitude}
                onChange={e => setForm({ ...form, longitude: Number(e.target.value) })}
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 font-mono text-sm outline-none focus:border-emerald-700"
              />
            </label>

            <label className="block sm:col-span-2">
              <span className="text-sm font-medium text-stone-700">Qeyd</span>
              <textarea
                value={form.note ?? ''}
                onChange={e => setForm({ ...form, note: e.target.value || null })}
                rows={2}
                maxLength={2000}
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
              />
            </label>
          </div>

          <div className="mt-5 flex gap-2">
            <button
              type="submit"
              disabled={busy}
              className="rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-50"
            >
              {busy ? 'Saxlanılır…' : 'Saxla'}
            </button>
            <button
              type="button"
              onClick={() => setForm(null)}
              className="rounded border border-stone-300 px-4 py-2 text-sm hover:bg-stone-50"
            >
              Ləğv et
            </button>
          </div>
          {(create.isError || update.isError) && (
            <p className="mt-2 text-sm text-red-700">
              {((create.error ?? update.error) as Error).message}
            </p>
          )}
        </form>
      )}

      <div className="responsive-table sites-table mt-6 overflow-x-auto rounded-lg border border-stone-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-stone-200 text-left text-xs uppercase tracking-wide text-stone-400">
              <th className="px-3 py-2">Obyekt</th>
              <th className="px-3 py-2">Növ</th>
              <th className="px-3 py-2">Vahidlər</th>
              <th className="px-3 py-2">Əlaqə</th>
              <th className="px-3 py-2"></th>
            </tr>
          </thead>
          <tbody>
            {rows.map(site => (
              <tr
                key={site.id}
                onClick={() => setSelectedId(site.id)}
                className={`cursor-pointer border-b border-stone-100 last:border-0 hover:bg-stone-50 ${
                  selectedId === site.id ? 'bg-emerald-50/60' : ''
                }`}
              >
                <td data-label="Obyekt" className="px-3 py-2">
                  <span className="font-medium">{site.name}</span>
                  {site.address && (
                    <span className="block text-xs text-stone-400">{site.address}</span>
                  )}
                </td>
                <td data-label="Növ" className="px-3 py-2 text-stone-600">
                  {SITE_KINDS.find(k => k.value === site.kind)?.label ?? site.kind}
                </td>
                <td data-label="Vahidlər" className="px-3 py-2">
                  {site.items.length === 0 ? (
                    <span className="text-stone-400">—</span>
                  ) : (
                    <span>
                      {site.totalQuantity} vahid
                      <span className="block text-xs text-stone-400">
                        {site.items.map(i => `${i.quantity} × ${i.productName}`).join(', ')}
                      </span>
                    </span>
                  )}
                </td>
                <td data-label="Əlaqə" className="px-3 py-2 text-stone-600">
                  {site.contactName ?? '—'}
                  {site.contactPhone && (
                    <span className="block text-xs text-stone-400">{site.contactPhone}</span>
                  )}
                </td>
                <td data-label="Əməliyyatlar" className="mobile-actions px-3 py-2 text-right whitespace-nowrap">
                  <Link
                    to={`/vahidler?obyekt=${site.id}`}
                    onClick={event => event.stopPropagation()}
                    className="rounded px-2 py-1 text-xs text-emerald-800 hover:bg-emerald-50"
                  >
                    Vahidlər
                  </Link>
                  <button
                    type="button"
                    onClick={event => {
                      event.stopPropagation()
                      startEdit(site)
                    }}
                    className="rounded px-2 py-1 text-xs text-emerald-800 hover:bg-emerald-50"
                  >
                    Redaktə
                  </button>
                  <button
                    type="button"
                    onClick={event => {
                      event.stopPropagation()
                      if (
                        confirm(
                          `«${site.name}» silinsin? Vahidlər silinmir — anbara qaytarılır.`,
                        )
                      )
                        remove.mutate(site.id)
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
                <td data-empty="true" colSpan={5} className="px-3 py-6 text-center text-stone-400">
                  Obyekt yoxdur. «Yeni obyekt» ilə başlayın.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  )
}
