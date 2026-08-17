import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useCategories } from '../api/categories'
import { useProducts, type ProductListItem } from '../api/products'

function StatusPill({ status }: { status: ProductListItem['status'] }) {
  const styles = {
    Draft: 'bg-amber-50 text-amber-900',
    Published: 'bg-emerald-50 text-emerald-900',
    Archived: 'bg-stone-100 text-stone-500',
  }[status]
  const labels = { Draft: 'Qaralama', Published: 'Dərc olunub', Archived: 'Arxiv' }[status]
  return <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${styles}`}>{labels}</span>
}

export default function Products() {
  const [search, setSearch] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)

  const categories = useCategories()
  const products = useProducts(search, categoryId, status, page)

  const totalPages = products.data
    ? Math.max(1, Math.ceil(products.data.total / products.data.pageSize))
    : 1

  return (
    <div className="admin-page products-page">
      <div className="page-heading flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-lg font-semibold tracking-tight">Məhsullar</h1>
          <p className="mt-1 text-sm text-stone-500">
            Hər rəng/ölçü ayrı məhsuldur — bənzərini «Kopyala» ilə yaradın.
          </p>
        </div>
        <Link
          to="/mehsullar/yeni"
          className="page-primary-action rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700"
        >
          Yeni məhsul
        </Link>
      </div>

      <div className="admin-filters mt-4 flex flex-wrap gap-2">
        <input
          value={search}
          onChange={e => { setSearch(e.target.value); setPage(1) }}
          placeholder="Ad və ya SKU üzrə axtar"
          className="w-full max-w-xs rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
        />
        <select
          value={categoryId}
          onChange={e => { setCategoryId(e.target.value); setPage(1) }}
          className="rounded border border-stone-300 px-3 py-2 text-sm"
        >
          <option value="">Bütün kateqoriyalar</option>
          {categories.data?.map(c => (
            <option key={c.id} value={c.id}>{c.name}</option>
          ))}
        </select>
        <select
          value={status}
          onChange={e => { setStatus(e.target.value); setPage(1) }}
          className="rounded border border-stone-300 px-3 py-2 text-sm"
        >
          <option value="">Bütün statuslar</option>
          <option value="draft">Qaralama</option>
          <option value="published">Dərc olunub</option>
          <option value="archived">Arxiv</option>
        </select>
      </div>

      <div className="responsive-table product-table mt-4 overflow-x-auto rounded-lg border border-stone-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-stone-200 text-left text-xs uppercase tracking-wide text-stone-400">
              <th className="w-14 px-3 py-2"></th>
              <th className="px-3 py-2">Ad</th>
              <th className="px-3 py-2">Kateqoriya</th>
              <th className="px-3 py-2">SKU</th>
              <th className="px-3 py-2">Status</th>
            </tr>
          </thead>
          <tbody>
            {products.data?.items.map(p => (
              <tr key={p.id} className="border-b border-stone-100 last:border-0 hover:bg-stone-50">
                <td data-label="Şəkil" className="px-3 py-2">
                  {p.thumbnailUrl ? (
                    <img src={p.thumbnailUrl} alt="" className="h-9 w-9 rounded object-cover" />
                  ) : (
                    <div className="h-9 w-9 rounded bg-stone-100" />
                  )}
                </td>
                <td data-label="Məhsul" className="px-3 py-2">
                  <Link to={`/mehsullar/${p.id}`} className="font-medium text-emerald-900 hover:underline">
                    {p.name}
                  </Link>
                </td>
                <td data-label="Kateqoriya" className="px-3 py-2 text-stone-500">{p.categoryName}</td>
                <td data-label="SKU" className="px-3 py-2 font-mono text-xs text-stone-500">{p.sku ?? '—'}</td>
                <td data-label="Status" className="px-3 py-2"><StatusPill status={p.status} /></td>
              </tr>
            ))}
            {products.data && products.data.items.length === 0 && (
              <tr>
                <td data-empty="true" colSpan={5} className="px-3 py-8 text-center text-sm text-stone-400">
                  Məhsul tapılmadı.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {products.data && products.data.total > products.data.pageSize && (
        <div className="mt-3 flex items-center gap-3 text-sm">
          <button type="button" disabled={page <= 1} onClick={() => setPage(p => p - 1)}
            className="rounded border border-stone-300 px-3 py-1 disabled:opacity-40">‹ Əvvəlki</button>
          <span className="tabular-nums text-stone-500">{page} / {totalPages}</span>
          <button type="button" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}
            className="rounded border border-stone-300 px-3 py-1 disabled:opacity-40">Növbəti ›</button>
        </div>
      )}

      {products.isError && (
        <p role="alert" className="mt-3 rounded bg-red-50 px-3 py-2 text-sm text-red-800">
          {products.error.message}
        </p>
      )}
    </div>
  )
}
