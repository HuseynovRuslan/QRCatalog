import { useEffect, useState, type ReactNode } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useLogout, useMe } from '../api/auth'
import { useDashboard } from '../api/stats'

type IconName = 'dashboard' | 'products' | 'categories' | 'qr' | 'sites' | 'units' | 'inquiries' | 'settings' | 'menu' | 'logout'

function Icon({ name, size = 19 }: { name: IconName; size?: number }) {
  const paths: Record<IconName, ReactNode> = {
    dashboard: <><rect x="3" y="3" width="7" height="7" rx="2" /><rect x="14" y="3" width="7" height="11" rx="2" /><rect x="3" y="14" width="7" height="7" rx="2" /><rect x="14" y="18" width="7" height="3" rx="1.5" /></>,
    products: <><path d="m12 3 8.5 4.5L12 12 3.5 7.5 12 3Z" /><path d="M3.5 7.5V17L12 21.5l8.5-4.5V7.5" /><path d="M12 12v9.5" /></>,
    categories: <><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 12 9 5 9-5" /><path d="m3 16 9 5 9-5" /></>,
    qr: <><rect x="3" y="3" width="6" height="6" rx="1" /><rect x="15" y="3" width="6" height="6" rx="1" /><rect x="3" y="15" width="6" height="6" rx="1" /><path d="M15 15h2v2h-2zM19 15h2M19 19h2v2h-2zM15 19v2" /></>,
    sites: <><path d="M20 10c0 5-8 11-8 11S4 15 4 10a8 8 0 1 1 16 0Z" /><circle cx="12" cy="10" r="2.5" /></>,
    units: <><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /></>,
    inquiries: <><path d="M21 15a4 4 0 0 1-4 4H8l-5 3V7a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4v8Z" /><path d="M8 9h8M8 13h5" /></>,
    settings: <><path d="M4 6h7M15 6h5M4 12h3M11 12h9M4 18h9M17 18h3" /><circle cx="13" cy="6" r="2" /><circle cx="9" cy="12" r="2" /><circle cx="15" cy="18" r="2" /></>,
    menu: <><path d="M4 7h16M4 12h16M4 17h16" /></>,
    logout: <><path d="M10 17l5-5-5-5M15 12H3" /><path d="M14 3h4a3 3 0 0 1 3 3v12a3 3 0 0 1-3 3h-4" /></>,
  }
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>
}

const navGroups: { label: string; items: { to: string; label: string; icon: IconName }[] }[] = [
  { label: 'Kataloq', items: [
    { to: '/', label: 'İcmal', icon: 'dashboard' }, { to: '/mehsullar', label: 'Məhsullar', icon: 'products' },
    { to: '/kateqoriyalar', label: 'Kateqoriyalar', icon: 'categories' }, { to: '/qr', label: 'QR kodlar', icon: 'qr' },
  ] },
  { label: 'Əməliyyat', items: [
    { to: '/obyektler', label: 'Obyektlər', icon: 'sites' }, { to: '/vahidler', label: 'Vahidlər', icon: 'units' },
    { to: '/muracietler', label: 'Müraciətlər', icon: 'inquiries' },
  ] },
  { label: 'Sistem', items: [{ to: '/parametrler', label: 'Parametrlər', icon: 'settings' }] },
]

const pageTitles: Record<string, string> = {
  '/': 'İdarəetmə icmalı', '/mehsullar': 'Məhsullar', '/kateqoriyalar': 'Kateqoriyalar', '/qr': 'QR kodlar',
  '/obyektler': 'Obyektlər', '/vahidler': 'Vahidlər', '/muracietler': 'Müraciətlər', '/parametrler': 'Parametrlər',
}

export default function Shell() {
  const me = useMe()
  const dashboard = useDashboard()
  const logout = useLogout()
  const navigate = useNavigate()
  const location = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)

  useEffect(() => setMenuOpen(false), [location.pathname])

  const currentTitle = location.pathname.startsWith('/mehsullar/') ? 'Məhsul redaktoru' : pageTitles[location.pathname] ?? 'QrCatalog'
  const displayName = me.data?.displayName || me.data?.email?.split('@')[0] || 'Admin'
  const initial = displayName.trim().charAt(0).toUpperCase()

  return (
    <div className="admin-shell">
      {menuOpen && <button className="sidebar-scrim" aria-label="Menyunu bağla" onClick={() => setMenuOpen(false)} />}
      <aside className={`admin-sidebar ${menuOpen ? 'is-open' : ''}`}>
        <div className="admin-brand"><span className="admin-brand-mark">Q</span><span><strong>QrCatalog</strong><small>FURNITURE SYSTEM</small></span></div>
        <div className="sidebar-status"><span className="status-pulse" /><span>Sistem aktivdir</span><small>canlı</small></div>
        <nav className="admin-nav" aria-label="Əsas naviqasiya">
          {navGroups.map(group => <div className="nav-group" key={group.label}>
            <p className="nav-group-label">{group.label}</p>
            {group.items.map(item => <NavLink key={item.to} to={item.to} end={item.to === '/'} className={({ isActive }) => `admin-nav-link ${isActive ? 'is-active' : ''}`}>
              <span className="nav-icon"><Icon name={item.icon} /></span><span>{item.label}</span>
              {item.to === '/muracietler' && (dashboard.data?.newInquiries ?? 0) > 0 && <span className="nav-badge">{dashboard.data?.newInquiries}</span>}
            </NavLink>)}
          </div>)}
        </nav>
        <div className="sidebar-profile">
          <span className="profile-avatar">{initial}</span><span className="profile-copy"><strong>{displayName}</strong><small>{me.data?.email}</small></span>
          <button type="button" className="profile-logout" aria-label="Çıxış" title="Çıxış" onClick={() => logout.mutate(undefined, { onSettled: () => navigate('/login', { replace: true }) })}><Icon name="logout" size={18} /></button>
        </div>
      </aside>
      <div className="admin-workspace">
        <header className="admin-topbar">
          <div className="topbar-title"><button type="button" className="mobile-menu" aria-label="Menyunu aç" onClick={() => setMenuOpen(true)}><Icon name="menu" size={22} /></button><span className="topbar-kicker">Admin panel</span><strong>{currentTitle}</strong></div>
          <div className="topbar-meta"><span className="topbar-date">{new Intl.DateTimeFormat('az', { day: 'numeric', month: 'long', year: 'numeric' }).format(new Date())}</span><a className="view-catalog" href="/" target="_blank" rel="noreferrer">Kataloqa bax <span aria-hidden="true">↗</span></a></div>
        </header>
        <main className="admin-main"><Outlet /></main>
      </div>
    </div>
  )
}
