import { useMe } from '../api/auth'

export default function Dashboard() {
  const me = useMe()

  return (
    <div>
      <h1 className="text-lg font-semibold tracking-tight">Panel</h1>
      <p className="mt-1 text-sm text-stone-500">
        Xoş gəldiniz, {me.data?.displayName || me.data?.email}.
      </p>
      <div className="mt-6 grid gap-4 sm:grid-cols-3">
        {['Məhsul', 'Skan (30 gün)', 'Müraciət'].map(label => (
          <div key={label} className="rounded-lg border border-stone-200 bg-white p-4">
            <p className="text-xs uppercase tracking-wide text-stone-400">{label}</p>
            <p className="mt-1 text-2xl font-semibold tabular-nums">—</p>
            <p className="mt-1 text-xs text-stone-400">Növbəti mərhələlərdə dolacaq</p>
          </div>
        ))}
      </div>
    </div>
  )
}
