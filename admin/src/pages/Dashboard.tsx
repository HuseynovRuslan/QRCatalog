import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMe } from '../api/auth'
import { useDashboard, useScanReport } from '../api/stats'

function StatTile({ label, value, hint }: { label: string; value: number | string; hint?: string }) {
  return (
    <div className="rounded-lg border border-stone-200 bg-white p-4">
      <p className="text-xs uppercase tracking-wide text-stone-400">{label}</p>
      <p className="mt-1 text-2xl font-semibold tabular-nums">{value}</p>
      {hint && <p className="mt-1 text-xs text-stone-400">{hint}</p>}
    </div>
  )
}

/* Kitabxanasız sadə sütun qrafiki — Recharts F2-də gələcək */
function DayBars({ days }: { days: { date: string; count: number }[] }) {
  if (days.length === 0)
    return <p className="py-6 text-center text-sm text-stone-400">Hələ skan yoxdur.</p>

  const max = Math.max(...days.map(d => d.count))
  return (
    <div className="flex h-28 items-end gap-1">
      {days.map(d => (
        <div
          key={d.date}
          title={`${new Date(d.date).toLocaleDateString('az')}: ${d.count}`}
          className="min-w-1 flex-1 rounded-t bg-emerald-700/80 transition hover:bg-emerald-600"
          style={{ height: `${max === 0 ? 2 : Math.max(4, (d.count / max) * 100)}%` }}
        />
      ))}
    </div>
  )
}

export default function Dashboard() {
  const me = useMe()
  const dashboard = useDashboard()
  const [days, setDays] = useState(30)
  const report = useScanReport(days)

  return (
    <div>
      <h1 className="text-lg font-semibold tracking-tight">Panel</h1>
      <p className="mt-1 text-sm text-stone-500">
        Xoş gəldiniz, {me.data?.displayName || me.data?.email}.
      </p>

      <div className="mt-6 grid gap-4 sm:grid-cols-4">
        <StatTile
          label="Məhsul"
          value={dashboard.data?.productsTotal ?? '—'}
          hint={dashboard.data ? `${dashboard.data.productsPublished} dərc olunub` : undefined}
        />
        <StatTile label="Skan (30 gün)" value={dashboard.data?.scans30d ?? '—'} />
        <StatTile
          label="Yeni müraciət"
          value={dashboard.data?.newInquiries ?? '—'}
          hint={dashboard.data && dashboard.data.newInquiries > 0 ? 'cavab gözləyir' : undefined}
        />
        <StatTile
          label="Skan olunmayan kod"
          value={dashboard.data?.unscannedCodes ?? '—'}
          hint="etiket vurulmayıb və ya görünmür"
        />
      </div>

      <div className="mt-6 rounded-lg border border-stone-200 bg-white p-4">
        <div className="flex items-center justify-between">
          <h2 className="text-sm font-semibold">Skan dinamikası</h2>
          <select
            value={days}
            onChange={e => setDays(Number(e.target.value))}
            className="rounded border border-stone-300 px-2 py-1 text-xs"
          >
            <option value={7}>7 gün</option>
            <option value={30}>30 gün</option>
            <option value={90}>90 gün</option>
          </select>
        </div>
        <div className="mt-3">
          {report.data && <DayBars days={report.data.byDay} />}
        </div>
      </div>

      <div className="mt-6 grid gap-4 lg:grid-cols-2">
        <div className="rounded-lg border border-stone-200 bg-white p-4">
          <h2 className="text-sm font-semibold">Ən çox skan olunanlar (30 gün)</h2>
          {dashboard.data && dashboard.data.topProducts.length === 0 && (
            <p className="py-4 text-sm text-stone-400">Hələ məhsul skanı yoxdur.</p>
          )}
          <ul className="mt-2 divide-y divide-stone-100">
            {dashboard.data?.topProducts.map(top => (
              <li key={top.name} className="flex justify-between py-2 text-sm">
                <span>{top.name}</span>
                <span className="font-mono tabular-nums text-emerald-800">{top.count}</span>
              </li>
            ))}
          </ul>
        </div>

        <div className="rounded-lg border border-stone-200 bg-white p-4">
          <h2 className="text-sm font-semibold">Kod üzrə skanlar</h2>
          {report.data && report.data.byCode.length === 0 && (
            <p className="py-4 text-sm text-stone-400">Seçilən dövrdə skan yoxdur.</p>
          )}
          <ul className="mt-2 max-h-64 divide-y divide-stone-100 overflow-y-auto">
            {report.data?.byCode.map(code => (
              <li key={code.humanCode} className="flex justify-between gap-2 py-2 text-sm">
                <span className="font-mono text-xs">{code.humanCode}</span>
                <span className="flex-1 truncate text-stone-500">{code.productName ?? '—'}</span>
                <span className="font-mono tabular-nums text-emerald-800">{code.count}</span>
              </li>
            ))}
          </ul>
        </div>
      </div>

      {report.data && report.data.unscanned.length > 0 && (
        <div className="mt-6 rounded-lg border border-amber-200 bg-amber-50/60 p-4">
          <h2 className="text-sm font-semibold text-amber-900">
            Heç vaxt skan olunmayan kodlar ({report.data.unscanned.length})
          </h2>
          <p className="mt-1 text-xs text-amber-800/80">
            Etiket çap olunub vurulmayıb, ya da görünməyən yerdədir —{' '}
            <Link to="/qr" className="underline">QR kodlar</Link> səhifəsindən yoxlayın.
          </p>
          <div className="mt-2 flex flex-wrap gap-1.5">
            {report.data.unscanned.map(code => (
              <span key={code.humanCode}
                className="rounded border border-amber-200 bg-white px-2 py-0.5 font-mono text-xs">
                {code.humanCode}
              </span>
            ))}
          </div>
        </div>
      )}

      {(dashboard.isError || report.isError) && (
        <p role="alert" className="mt-4 rounded bg-red-50 px-3 py-2 text-sm text-red-800">
          {dashboard.error?.message ?? report.error?.message}
        </p>
      )}
    </div>
  )
}
