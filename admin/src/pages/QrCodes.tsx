import { useState } from 'react'
import { useCategories } from '../api/categories'
import {
  useActivateQrCode,
  useCreateQrCode,
  useQrCodes,
  useRetireQrCode,
  useSheetDownload,
  type QrCode,
} from '../api/qrcodes'

function CreateDialog({ onClose }: { onClose: () => void }) {
  const categories = useCategories()
  const create = useCreateQrCode()
  const [targetType, setTargetType] = useState<'category' | 'archive'>('category')
  const [targetId, setTargetId] = useState('')

  return (
    <div className="fixed inset-0 z-10 flex items-center justify-center bg-stone-900/40 px-4">
      <form
        onSubmit={e => {
          e.preventDefault()
          create.mutate(
            { targetType, targetId: targetType === 'archive' ? null : targetId },
            { onSuccess: onClose },
          )
        }}
        className="w-full max-w-md rounded-lg border border-stone-200 bg-white p-6 shadow-lg"
      >
        <h2 className="text-base font-semibold">Yeni QR kod</h2>
        <p className="mt-1 text-sm text-stone-500">
          Kod yaradılandan sonra silinmir — çap olunmuş etiket geri qaytarıla bilməz.
        </p>

        <label className="mt-4 block">
          <span className="text-sm font-medium text-stone-700">Hədəf növü</span>
          <select
            value={targetType}
            onChange={e => setTargetType(e.target.value as 'category' | 'archive')}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm"
          >
            <option value="category">Kateqoriya</option>
            <option value="archive">Arxiv səhifəsi</option>
          </select>
        </label>

        {targetType === 'category' && (
          <label className="mt-3 block">
            <span className="text-sm font-medium text-stone-700">Kateqoriya</span>
            <select
              required
              value={targetId}
              onChange={e => setTargetId(e.target.value)}
              className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm"
            >
              <option value="">— seçin —</option>
              {categories.data?.map(c => (
                <option key={c.id} value={c.id}>
                  {c.name}
                  {c.codePrefix ? ` (${c.codePrefix})` : ''}
                </option>
              ))}
            </select>
          </label>
        )}

        {create.isError && (
          <p role="alert" className="mt-3 rounded bg-red-50 px-3 py-2 text-sm text-red-800">
            {create.error.message}
          </p>
        )}

        <div className="mt-5 flex justify-end gap-2">
          <button type="button" onClick={onClose}
            className="rounded border border-stone-300 px-3 py-1.5 text-sm hover:bg-stone-50">
            İmtina
          </button>
          <button type="submit" disabled={create.isPending}
            className="rounded bg-emerald-800 px-4 py-1.5 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-60">
            {create.isPending ? 'Yaradılır…' : 'Yarat'}
          </button>
        </div>
      </form>
    </div>
  )
}

function StatusPill({ status }: { status: QrCode['status'] }) {
  return status === 'Active' ? (
    <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-900">
      Aktiv
    </span>
  ) : (
    <span className="rounded-full bg-stone-100 px-2 py-0.5 text-xs font-medium text-stone-500">
      Dayandırılıb
    </span>
  )
}

export default function QrCodes() {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [showCreate, setShowCreate] = useState(false)

  const codes = useQrCodes(search, page)
  const retire = useRetireQrCode()
  const activate = useActivateQrCode()
  const sheet = useSheetDownload()

  function toggle(id: string) {
    setSelected(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const totalPages = codes.data ? Math.max(1, Math.ceil(codes.data.total / codes.data.pageSize)) : 1

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-lg font-semibold tracking-tight">QR kodlar</h1>
          <p className="mt-1 text-sm text-stone-500">
            Kod seç → «Çap vərəqi» A4 PDF hazırlayır (3×7 etiket).
          </p>
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            disabled={selected.size === 0 || sheet.isPending}
            onClick={() => sheet.mutate([...selected])}
            className="rounded border border-emerald-800 px-4 py-2 text-sm font-medium text-emerald-900 hover:bg-emerald-50 disabled:opacity-40"
          >
            {sheet.isPending ? 'Hazırlanır…' : `Çap vərəqi (${selected.size})`}
          </button>
          <button
            type="button"
            onClick={() => setShowCreate(true)}
            className="rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700"
          >
            Yeni kod
          </button>
        </div>
      </div>

      <input
        value={search}
        onChange={e => {
          setSearch(e.target.value)
          setPage(1)
        }}
        placeholder="Kod üzrə axtar: SZ-0001"
        className="mt-4 w-full max-w-xs rounded border border-stone-300 px-3 py-2 font-mono text-sm uppercase outline-none focus:border-emerald-700"
      />

      <div className="mt-4 overflow-x-auto rounded-lg border border-stone-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-stone-200 text-left text-xs uppercase tracking-wide text-stone-400">
              <th className="w-8 px-3 py-2"></th>
              <th className="px-3 py-2">Kod</th>
              <th className="px-3 py-2">Hədəf</th>
              <th className="px-3 py-2">Status</th>
              <th className="px-3 py-2">Yükləmə</th>
              <th className="px-3 py-2"></th>
            </tr>
          </thead>
          <tbody>
            {codes.data?.items.map(qr => (
              <tr key={qr.id} className="border-b border-stone-100 last:border-0 hover:bg-stone-50">
                <td className="px-3 py-2">
                  <input
                    type="checkbox"
                    checked={selected.has(qr.id)}
                    onChange={() => toggle(qr.id)}
                    aria-label={`${qr.humanCode} seç`}
                  />
                </td>
                <td className="px-3 py-2 font-mono font-medium">{qr.humanCode}</td>
                <td className="px-3 py-2">
                  {qr.targetType === 'Archive' ? (
                    <span className="text-stone-400">Arxiv</span>
                  ) : (
                    (qr.targetName ?? <span className="text-stone-400">—</span>)
                  )}
                </td>
                <td className="px-3 py-2">
                  <StatusPill status={qr.status} />
                </td>
                <td className="px-3 py-2">
                  <a href={`/api/admin/qrcodes/${qr.id}/image.svg`} target="_blank" rel="noreferrer"
                    className="text-emerald-800 hover:underline">SVG</a>
                  <span className="text-stone-300"> · </span>
                  <a href={`/api/admin/qrcodes/${qr.id}/image.png`}
                    className="text-emerald-800 hover:underline">PNG</a>
                </td>
                <td className="px-3 py-2 text-right">
                  {qr.status === 'Active' ? (
                    <button type="button" onClick={() => retire.mutate(qr.id)}
                      className="rounded px-2 py-1 text-xs text-stone-500 hover:bg-stone-100">
                      Dayandır
                    </button>
                  ) : (
                    <button type="button" onClick={() => activate.mutate(qr.id)}
                      className="rounded px-2 py-1 text-xs text-emerald-800 hover:bg-emerald-50">
                      Aktivləşdir
                    </button>
                  )}
                </td>
              </tr>
            ))}
            {codes.data && codes.data.items.length === 0 && (
              <tr>
                <td colSpan={6} className="px-3 py-8 text-center text-sm text-stone-400">
                  {search ? 'Axtarışa uyğun kod tapılmadı.' : 'Hələ kod yoxdur — «Yeni kod» ilə başlayın.'}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {codes.data && codes.data.total > codes.data.pageSize && (
        <div className="mt-3 flex items-center gap-3 text-sm">
          <button type="button" disabled={page <= 1} onClick={() => setPage(p => p - 1)}
            className="rounded border border-stone-300 px-3 py-1 disabled:opacity-40">
            ‹ Əvvəlki
          </button>
          <span className="tabular-nums text-stone-500">{page} / {totalPages}</span>
          <button type="button" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}
            className="rounded border border-stone-300 px-3 py-1 disabled:opacity-40">
            Növbəti ›
          </button>
        </div>
      )}

      {(codes.isError || sheet.isError) && (
        <p role="alert" className="mt-3 rounded bg-red-50 px-3 py-2 text-sm text-red-800">
          {codes.error?.message ?? sheet.error?.message}
        </p>
      )}

      {showCreate && <CreateDialog onClose={() => setShowCreate(false)} />}
    </div>
  )
}
