import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useLogout, useMe } from '../api/auth'

const nav = [
  { to: '/', label: 'Panel', enabled: true },
  { to: '/mehsullar', label: 'Məhsullar', enabled: false },
  { to: '/kateqoriyalar', label: 'Kateqoriyalar', enabled: true },
  { to: '/qr', label: 'QR kodlar', enabled: false },
  { to: '/muracietler', label: 'Müraciətlər', enabled: false },
]

export default function Shell() {
  const me = useMe()
  const logout = useLogout()
  const navigate = useNavigate()

  return (
    <div className="flex min-h-screen bg-stone-100 text-stone-900">
      <aside className="flex w-56 flex-col border-r border-stone-200 bg-white">
        <div className="border-b border-stone-200 px-4 py-4">
          <span className="text-sm font-semibold tracking-tight">QrCatalog</span>
        </div>
        <nav className="flex-1 space-y-1 p-2">
          {nav.map(item =>
            item.enabled ? (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.to === '/'}
                className={({ isActive }) =>
                  `block rounded px-3 py-2 text-sm ${
                    isActive
                      ? 'bg-emerald-50 font-medium text-emerald-900'
                      : 'text-stone-600 hover:bg-stone-50'
                  }`
                }
              >
                {item.label}
              </NavLink>
            ) : (
              <span
                key={item.to}
                title="Növbəti mərhələdə"
                className="block cursor-not-allowed rounded px-3 py-2 text-sm text-stone-300"
              >
                {item.label}
              </span>
            ),
          )}
        </nav>
      </aside>

      <div className="flex flex-1 flex-col">
        <header className="flex items-center justify-between border-b border-stone-200 bg-white px-6 py-3">
          <span className="text-sm text-stone-500">{me.data?.email}</span>
          <button
            type="button"
            onClick={() =>
              logout.mutate(undefined, { onSettled: () => navigate('/login', { replace: true }) })
            }
            className="rounded border border-stone-300 px-3 py-1.5 text-sm text-stone-700 hover:bg-stone-50"
          >
            Çıxış
          </button>
        </header>
        <main className="flex-1 p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
