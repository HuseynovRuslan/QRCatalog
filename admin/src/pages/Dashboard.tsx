import { useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { useMe } from '../api/auth'
import { useDashboard, useScanReport } from '../api/stats'

type StatTone = 'forest' | 'honey' | 'sage' | 'clay'

function StatTile({ label, value, hint, tone, icon }: {
  label: string
  value: number | string
  hint?: string
  tone: StatTone
  icon: ReactNode
}) {
  return (
    <article className={`stat-card stat-${tone}`}>
      <div className="stat-card-top">
        <span className="stat-icon">{icon}</span>
        <span className="stat-trend">son 30 gün</span>
      </div>
      <p className="stat-value">{value}</p>
      <p className="stat-label">{label}</p>
      <p className="stat-hint">{hint ?? 'Məlumat yenilənir'}</p>
    </article>
  )
}

function LineIcon({ children }: { children: ReactNode }) {
  return <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{children}</svg>
}

function DayBars({ days }: { days: { date: string; count: number }[] }) {
  if (days.length === 0) return <div className="chart-empty"><span>↗</span><p>Hələ skan yoxdur.</p></div>

  const max = Math.max(...days.map(day => day.count), 1)
  const labelEvery = Math.max(1, Math.ceil(days.length / 6))

  return (
    <div className="chart-wrap">
      <div className="chart-grid"><span /><span /><span /><span /></div>
      <div className="day-bars">
        {days.map((day, index) => (
          <div className="day-column" key={day.date}>
            <div className="day-bar-track">
              <div
                className="day-bar"
                title={`${new Date(day.date).toLocaleDateString('az')}: ${day.count}`}
                style={{ height: `${Math.max(5, (day.count / max) * 100)}%` }}
              >
                {day.count > 0 && <span>{day.count}</span>}
              </div>
            </div>
            <small>{index % labelEvery === 0 ? new Date(day.date).toLocaleDateString('az', { day: '2-digit', month: 'short' }) : ''}</small>
          </div>
        ))}
      </div>
    </div>
  )
}

export default function Dashboard() {
  const me = useMe()
  const dashboard = useDashboard()
  const [days, setDays] = useState(30)
  const report = useScanReport(days)
  const firstName = (me.data?.displayName || me.data?.email?.split('@')[0] || 'Admin').split(' ')[0]
  const publishedPercent = dashboard.data?.productsTotal
    ? Math.round((dashboard.data.productsPublished / dashboard.data.productsTotal) * 100)
    : 0
  const maxTop = Math.max(...(dashboard.data?.topProducts.map(item => item.count) ?? [1]), 1)

  return (
    <div className="dashboard-page">
      <section className="dashboard-hero">
        <div className="dashboard-hero-copy">
          <span className="eyebrow">İDARƏETMƏ MƏRKƏZİ</span>
          <h1 aria-label="Panel">Salam, {firstName}.<br /><em>Hər şey nəzarət altındadır.</em></h1>
          <p>Kataloqun performansı, yeni müraciətlər və sahədəki bütün məhsullar bir baxışda.</p>
          <div className="hero-actions">
            <Link to="/mehsullar/yeni" className="hero-primary">+ Yeni məhsul</Link>
            <Link to="/muracietler" className="hero-secondary">Müraciətlərə bax <span>→</span></Link>
          </div>
        </div>
        <div className="hero-orbit" aria-hidden="true"><span className="orbit-one" /><span className="orbit-two" /><strong>QC</strong></div>
      </section>

      <section className="stats-grid" aria-label="Əsas göstəricilər">
        <StatTile
          label="Ümumi məhsul"
          value={dashboard.data?.productsTotal ?? '—'}
          hint={dashboard.data ? `${dashboard.data.productsPublished} məhsul dərc olunub · ${publishedPercent}%` : undefined}
          tone="forest"
          icon={<LineIcon><path d="m12 3 8 4.5-8 4.5-8-4.5L12 3Z" /><path d="M4 7.5V17l8 4 8-4V7.5M12 12v9" /></LineIcon>}
        />
        <StatTile
          label="QR skan"
          value={dashboard.data?.scans30d ?? '—'}
          hint="Məhsullara real maraq"
          tone="honey"
          icon={<LineIcon><path d="M4 17V9M10 17V5M16 17v-7M22 17H2" /></LineIcon>}
        />
        <StatTile
          label="Yeni müraciət"
          value={dashboard.data?.newInquiries ?? '—'}
          hint={(dashboard.data?.newInquiries ?? 0) > 0 ? 'Cavabınızı gözləyir' : 'Bütün müraciətlər cavablanıb'}
          tone="sage"
          icon={<LineIcon><path d="M21 15a4 4 0 0 1-4 4H8l-5 3V7a4 4 0 0 1 4-4h10a4 4 0 0 1 4 4v8Z" /><path d="M8 9h8M8 13h5" /></LineIcon>}
        />
        <StatTile
          label="Skan olunmayan"
          value={dashboard.data?.unscannedCodes ?? '—'}
          hint="Yoxlanmalı QR etiketləri"
          tone="clay"
          icon={<LineIcon><rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" /><path d="M3 14h7v7H3zM14 14h2v2h-2zM19 14h2M19 19h2M14 19v2" /></LineIcon>}
        />
      </section>

      <section className="dashboard-grid dashboard-grid-main">
        <article className="dashboard-card chart-card">
          <div className="card-heading">
            <div><span className="card-kicker">PERFORMANS</span><h2>Skan dinamikası</h2><p>Müştərilərin QR kodlarla qarşılıqlı əlaqəsi</p></div>
            <select value={days} onChange={event => setDays(Number(event.target.value))} aria-label="Hesabat müddəti">
              <option value={7}>Son 7 gün</option><option value={30}>Son 30 gün</option><option value={90}>Son 90 gün</option>
            </select>
          </div>
          <DayBars days={report.data?.byDay ?? []} />
        </article>

        <article className="dashboard-card attention-card">
          <div className="attention-icon"><LineIcon><path d="M12 9v4M12 17h.01" /><path d="M10.3 3.7 2.6 17a2 2 0 0 0 1.7 3h15.4a2 2 0 0 0 1.7-3L13.7 3.7a2 2 0 0 0-3.4 0Z" /></LineIcon></div>
          <span className="card-kicker">DİQQƏT TƏLƏB EDİR</span>
          <h2>{report.data?.unscanned.length ?? dashboard.data?.unscannedCodes ?? '—'} QR kod</h2>
          <p>Bu kodlar hələ heç vaxt skan edilməyib. Etiketlərin görünən yerdə olduğunu yoxlayın.</p>
          <Link to="/qr">Kodları yoxla <span>→</span></Link>
          <div className="attention-codes">
            {report.data?.unscanned.slice(0, 4).map(code => <span key={code.humanCode}>{code.humanCode}</span>)}
          </div>
        </article>
      </section>

      <section className="dashboard-grid dashboard-grid-bottom">
        <article className="dashboard-card ranking-card">
          <div className="card-heading"><div><span className="card-kicker">TOP MƏHSULLAR</span><h2>Ən çox maraq görənlər</h2></div><Link to="/mehsullar">Hamısına bax →</Link></div>
          {dashboard.data && dashboard.data.topProducts.length === 0 && <p className="list-empty">Hələ məhsul skanı yoxdur.</p>}
          <ol className="ranking-list">
            {dashboard.data?.topProducts.map((top, index) => <li key={top.name}>
              <span className="rank-number">{String(index + 1).padStart(2, '0')}</span>
              <div className="rank-copy"><div><strong>{top.name}</strong><span>{top.count} skan</span></div><div className="rank-track"><span style={{ width: `${(top.count / maxTop) * 100}%` }} /></div></div>
            </li>)}
          </ol>
        </article>

        <article className="dashboard-card code-card">
          <div className="card-heading"><div><span className="card-kicker">CANLI SİYAHI</span><h2>Kod üzrə skanlar</h2></div><span className="live-label"><i /> yenilənir</span></div>
          {report.data && report.data.byCode.length === 0 && <p className="list-empty">Seçilən dövrdə skan yoxdur.</p>}
          <ul className="code-list">
            {report.data?.byCode.slice(0, 7).map(code => <li key={code.humanCode}><span className="code-mark">⌁</span><div><strong>{code.humanCode}</strong><small>{code.productName ?? 'Məhsula bağlanmayıb'}</small></div><b>{code.count}</b></li>)}
          </ul>
        </article>
      </section>

      {(dashboard.isError || report.isError) && <p role="alert" className="dashboard-error">{dashboard.error?.message ?? report.error?.message}</p>}
    </div>
  )
}
