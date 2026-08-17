import { useMemo, useState, type FormEvent } from 'react'
import SiteMap from '../components/SiteMap'
import { useProducts } from '../api/products'
import {
  SITE_KINDS,
  useCreateSite,
  useDeleteSite,
  useReplaceSiteItems,
  useSites,
  useUpdateSite,
  type SaveSite,
  type Site,
  type SiteKind,
} from '../api/sites'

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

interface ItemRow {
  productId: string
  quantity: number
  installedOn: string | null
}

export default function Sites() {
  const [search, setSearch] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [form, setForm] = useState<(SaveSite & { id?: string }) | null>(null)
  const [rows, setRows] = useState<ItemRow[]>([])

  const sites = useSites(search)
  const products = useProducts('', '', '', 1)
  const create = useCreateSite()
  const update = useUpdateSite()
  const remove = useDeleteSite()
  const replaceItems = useReplaceSiteItems()

  const productOptions = products.data?.items ?? []
  const totals = useMemo(() => {
    const list = sites.data ?? []
    return {
      sites: list.length,
      units: list.reduce((sum, s) => sum + s.totalQuantity, 0),
    }
  }, [sites.data])

  function startCreate() {
    setForm({ ...EMPTY })
    setRows([])
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
    setRows(
      site.items.map(item => ({
        productId: item.productId,
        quantity: item.quantity,
        installedOn: item.installedOn,
      })),
    )
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

    const id = form.id ?? (await create.mutateAsync(payload)).id
    if (form.id) await update.mutateAsync({ id, ...payload })

    const clean = rows.filter(row => row.productId && row.quantity > 0)
    await replaceItems.mutateAsync({ id, items: clean })

    setForm(null)
    setRows([])
    setSelectedId(id)
  }

  const busy = create.isPending || update.isPending || replaceItems.isPending

  return (
    <div>
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">Obyektlər</h1>
          <p className="mt-1 text-sm text-stone-500">
            Məhsulların quraşdırıldığı yerlər. {totals.sites} obyekt, {totals.units} ədəd məhsul.
          </p>
        </div>
        <button
          type="button"
          onClick={startCreate}
          className="rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700"
        >
          Yeni obyekt
        </button>
      </div>

      <input
        value={search}
        onChange={e => setSearch(e.target.value)}
        placeholder="Ad və ya ünvan üzrə axtar"
        className="mt-4 w-full max-w-sm rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
      />

      <div className="mt-4">
        <SiteMap
          sites={sites.data ?? []}
          selectedId={selectedId}
          onSelect={setSelectedId}
          onPick={
            form
              ? (lat, lng) => setForm({ ...form, latitude: lat, longitude: lng })
              : undefined
          }
          draft={form ? { lat: form.latitude, lng: form.longitude } : null}
        />
        {form && (
          <p className="mt-2 text-sm text-emerald-800">
            Mövqeyi dəyişmək üçün xəritəyə klikləyin — qırmızı nöqtə seçilmiş yerdir.
          </p>
        )}
      </div>

      {form && (
        <form
          onSubmit={onSubmit}
          className="mt-4 rounded-lg border border-stone-200 bg-white p-4"
        >
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

          <h3 className="mt-5 text-sm font-semibold text-stone-700">Quraşdırılmış məhsullar</h3>
          <div className="mt-2 space-y-2">
            {rows.map((row, index) => (
              <div key={index} className="flex flex-wrap items-center gap-2">
                <select
                  value={row.productId}
                  onChange={e => {
                    const next = [...rows]
                    next[index] = { ...row, productId: e.target.value }
                    setRows(next)
                  }}
                  className="min-w-56 flex-1 rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
                >
                  <option value="">— məhsul seç —</option>
                  {productOptions.map(product => (
                    <option key={product.id} value={product.id}>
                      {product.name}
                    </option>
                  ))}
                </select>
                <input
                  type="number"
                  min={1}
                  value={row.quantity}
                  onChange={e => {
                    const next = [...rows]
                    next[index] = { ...row, quantity: Number(e.target.value) }
                    setRows(next)
                  }}
                  className="w-24 rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
                  aria-label="Ədəd"
                />
                <input
                  type="date"
                  value={row.installedOn ?? ''}
                  onChange={e => {
                    const next = [...rows]
                    next[index] = { ...row, installedOn: e.target.value || null }
                    setRows(next)
                  }}
                  className="rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
                  aria-label="Quraşdırma tarixi"
                />
                <button
                  type="button"
                  onClick={() => setRows(rows.filter((_, i) => i !== index))}
                  className="rounded px-2 py-1 text-xs text-stone-500 hover:bg-stone-100"
                >
                  Sil
                </button>
              </div>
            ))}
          </div>
          <button
            type="button"
            onClick={() => setRows([...rows, { productId: '', quantity: 1, installedOn: null }])}
            className="mt-2 rounded border border-stone-300 px-3 py-1.5 text-sm hover:bg-stone-50"
          >
            Məhsul sətri əlavə et
          </button>

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
              onClick={() => {
                setForm(null)
                setRows([])
              }}
              className="rounded border border-stone-300 px-4 py-2 text-sm hover:bg-stone-50"
            >
              Ləğv et
            </button>
          </div>
          {(create.isError || update.isError || replaceItems.isError) && (
            <p className="mt-2 text-sm text-red-700">
              {((create.error ?? update.error ?? replaceItems.error) as Error).message}
            </p>
          )}
        </form>
      )}

      <div className="mt-6 overflow-x-auto rounded-lg border border-stone-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-stone-200 text-left text-xs uppercase tracking-wide text-stone-400">
              <th className="px-3 py-2">Obyekt</th>
              <th className="px-3 py-2">Növ</th>
              <th className="px-3 py-2">Məhsullar</th>
              <th className="px-3 py-2">Əlaqə</th>
              <th className="px-3 py-2"></th>
            </tr>
          </thead>
          <tbody>
            {sites.data?.map(site => (
              <tr
                key={site.id}
                onClick={() => setSelectedId(site.id)}
                className={`cursor-pointer border-b border-stone-100 last:border-0 hover:bg-stone-50 ${
                  selectedId === site.id ? 'bg-emerald-50/60' : ''
                }`}
              >
                <td className="px-3 py-2">
                  <span className="font-medium">{site.name}</span>
                  {site.address && (
                    <span className="block text-xs text-stone-400">{site.address}</span>
                  )}
                </td>
                <td className="px-3 py-2 text-stone-600">
                  {SITE_KINDS.find(k => k.value === site.kind)?.label ?? site.kind}
                </td>
                <td className="px-3 py-2">
                  {site.items.length === 0 ? (
                    <span className="text-stone-400">—</span>
                  ) : (
                    <span>
                      {site.totalQuantity} ədəd
                      <span className="block text-xs text-stone-400">
                        {site.items.map(i => `${i.quantity} × ${i.productName}`).join(', ')}
                      </span>
                    </span>
                  )}
                </td>
                <td className="px-3 py-2 text-stone-600">
                  {site.contactName ?? '—'}
                  {site.contactPhone && (
                    <span className="block text-xs text-stone-400">{site.contactPhone}</span>
                  )}
                </td>
                <td className="px-3 py-2 text-right whitespace-nowrap">
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
                      if (confirm(`«${site.name}» silinsin?`)) remove.mutate(site.id)
                    }}
                    className="rounded px-2 py-1 text-xs text-stone-500 hover:bg-stone-100"
                  >
                    Sil
                  </button>
                </td>
              </tr>
            ))}
            {sites.data?.length === 0 && (
              <tr>
                <td colSpan={5} className="px-3 py-6 text-center text-stone-400">
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
