import { useState } from 'react'
import {
  useInquiries,
  useSetInquiryNote,
  useSetInquiryStatus,
  type Inquiry,
  type InquiryStatus,
} from '../api/inquiries'

const STATUS_LABELS: Record<InquiryStatus, string> = {
  New: 'Yeni',
  InProgress: 'Baxılır',
  Answered: 'Cavablandı',
  Closed: 'Bağlandı',
}

const STATUS_STYLES: Record<InquiryStatus, string> = {
  New: 'bg-amber-50 text-amber-900',
  InProgress: 'bg-blue-50 text-blue-900',
  Answered: 'bg-emerald-50 text-emerald-900',
  Closed: 'bg-stone-100 text-stone-500',
}

function InquiryRow({ inquiry }: { inquiry: Inquiry }) {
  const [open, setOpen] = useState(false)
  const [note, setNote] = useState(inquiry.internalNote ?? '')
  const setStatus = useSetInquiryStatus()
  const saveNote = useSetInquiryNote()

  return (
    <>
      <tr
        onClick={() => setOpen(o => !o)}
        className="cursor-pointer border-b border-stone-100 last:border-0 hover:bg-stone-50"
      >
        <td className="px-3 py-2 font-medium">{inquiry.name}</td>
        <td className="px-3 py-2 font-mono text-xs">{inquiry.phone}</td>
        <td className="px-3 py-2 text-stone-500">
          {inquiry.productName ?? <span className="text-stone-300">ümumi</span>}
          {inquiry.humanCode && (
            <span className="ml-1 font-mono text-xs text-emerald-800">{inquiry.humanCode}</span>
          )}
        </td>
        <td className="px-3 py-2">
          <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_STYLES[inquiry.status]}`}>
            {STATUS_LABELS[inquiry.status]}
          </span>
        </td>
        <td className="px-3 py-2 text-xs tabular-nums text-stone-400">
          {new Date(inquiry.createdAtUtc).toLocaleString('az')}
        </td>
      </tr>
      {open && (
        <tr className="border-b border-stone-100 bg-stone-50/60">
          <td colSpan={5} className="px-4 py-3">
            {inquiry.message && (
              <p className="whitespace-pre-wrap rounded bg-white p-3 text-sm text-stone-700">
                {inquiry.message}
              </p>
            )}

            <div className="mt-3 flex flex-wrap items-center gap-2">
              <span className="text-xs uppercase tracking-wide text-stone-400">Status:</span>
              {(Object.keys(STATUS_LABELS) as InquiryStatus[]).map(status => (
                <button
                  key={status}
                  type="button"
                  disabled={status === inquiry.status || setStatus.isPending}
                  onClick={() => setStatus.mutate({ id: inquiry.id, status })}
                  className={`rounded border px-2.5 py-1 text-xs ${
                    status === inquiry.status
                      ? 'border-emerald-800 bg-emerald-800 text-white'
                      : 'border-stone-300 hover:bg-white'
                  }`}
                >
                  {STATUS_LABELS[status]}
                </button>
              ))}
              <a href={`tel:${inquiry.phone}`}
                className="ml-auto rounded border border-emerald-800 px-2.5 py-1 text-xs text-emerald-900 hover:bg-emerald-50">
                Zəng et
              </a>
            </div>

            <div className="mt-3 flex gap-2">
              <input
                value={note}
                onChange={e => setNote(e.target.value)}
                placeholder="Daxili qeyd — müştəri görmür"
                className="flex-1 rounded border border-stone-300 px-3 py-1.5 text-sm"
              />
              <button
                type="button"
                disabled={saveNote.isPending}
                onClick={() => saveNote.mutate({ id: inquiry.id, note: note || null })}
                className="rounded border border-stone-300 px-3 py-1.5 text-sm hover:bg-white disabled:opacity-50"
              >
                Qeydi saxla
              </button>
            </div>
          </td>
        </tr>
      )}
    </>
  )
}

export default function Inquiries() {
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const inquiries = useInquiries(status, page)

  const totalPages = inquiries.data
    ? Math.max(1, Math.ceil(inquiries.data.total / inquiries.data.pageSize))
    : 1

  return (
    <div>
      <h1 className="text-lg font-semibold tracking-tight">Müraciətlər</h1>
      <p className="mt-1 text-sm text-stone-500">
        Public saytdakı sorğu formalarından gələnlər — mənbəyi (məhsul, QR kod) ilə birlikdə.
      </p>

      <select
        value={status}
        onChange={e => { setStatus(e.target.value); setPage(1) }}
        className="mt-4 rounded border border-stone-300 px-3 py-2 text-sm"
      >
        <option value="">Bütün statuslar</option>
        <option value="new">Yeni</option>
        <option value="inprogress">Baxılır</option>
        <option value="answered">Cavablandı</option>
        <option value="closed">Bağlandı</option>
      </select>

      <div className="mt-4 overflow-x-auto rounded-lg border border-stone-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-stone-200 text-left text-xs uppercase tracking-wide text-stone-400">
              <th className="px-3 py-2">Ad</th>
              <th className="px-3 py-2">Telefon</th>
              <th className="px-3 py-2">Mənbə</th>
              <th className="px-3 py-2">Status</th>
              <th className="px-3 py-2">Tarix</th>
            </tr>
          </thead>
          <tbody>
            {inquiries.data?.items.map(inquiry => (
              <InquiryRow key={inquiry.id} inquiry={inquiry} />
            ))}
            {inquiries.data && inquiries.data.items.length === 0 && (
              <tr>
                <td colSpan={5} className="px-3 py-8 text-center text-sm text-stone-400">
                  Müraciət yoxdur.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {inquiries.data && inquiries.data.total > inquiries.data.pageSize && (
        <div className="mt-3 flex items-center gap-3 text-sm">
          <button type="button" disabled={page <= 1} onClick={() => setPage(p => p - 1)}
            className="rounded border border-stone-300 px-3 py-1 disabled:opacity-40">‹ Əvvəlki</button>
          <span className="tabular-nums text-stone-500">{page} / {totalPages}</span>
          <button type="button" disabled={page >= totalPages} onClick={() => setPage(p => p + 1)}
            className="rounded border border-stone-300 px-3 py-1 disabled:opacity-40">Növbəti ›</button>
        </div>
      )}

      {inquiries.isError && (
        <p role="alert" className="mt-3 rounded bg-red-50 px-3 py-2 text-sm text-red-800">
          {inquiries.error.message}
        </p>
      )}
    </div>
  )
}
