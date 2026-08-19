import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useLogin } from '../api/auth'

/* Yalnız ÖZ saytımızın yolu qəbul edilir: kənar ünvan (`https://...`, `//evil.az`)
   parol oğurlama yönləndirməsinə çevrilə bilərdi. */
function safeReturnPath(raw: string | null): string | null {
  if (!raw) return null
  if (raw[0] !== '/' || raw[1] === '/' || raw[1] === String.fromCharCode(92)) return null
  return raw
}

export default function Login() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [rememberMe, setRememberMe] = useState(false)
  const login = useLogin()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'
  // Skamyadaki QR → müştəri səhifəsi → "İşçi girişi" → bura. Girişdən sonra HƏMİN
  // səhifəyə qayıdır; artıq girişli olduğu üçün Q səhifəsi admin ekranına yönləndirir.
  const returnPath = safeReturnPath(new URLSearchParams(window.location.search).get('qayit'))

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    login.mutate(
      { email, password, rememberMe },
      {
        onSuccess: () => {
          if (returnPath && !returnPath.startsWith('/admin')) {
            // SPA-dan kənar səhifə (məs. /q/token) — tam yüklənmə lazımdır
            window.location.assign(returnPath)
            return
          }
          navigate(returnPath ? returnPath.replace(/^\/admin/, '') || '/' : from, { replace: true })
        },
      },
    )
  }

  return (
    <main className="login-page">
      <section className="login-visual">
        <div className="login-brand">
          <img className="admin-brand-logo" src="/img/woodmark-cream.png" alt="" width={44} height={44} />
          <strong>WOODMARK</strong><small>İDARƏETMƏ PANELİ</small>
        </div>
        <div className="login-statement">
          <span className="eyebrow">AĞILLI KATALOQ İDARƏETMƏSİ</span>
          <h1>Məhsuldan<br /><em>müştəriyə,</em><br />bir toxunuşda.</h1>
          <p>Taxta mebel kolleksiyanızı, QR kodları və müştəri marağını vahid mərkəzdən idarə edin.</p>
        </div>
        <div className="login-pill-row"><span><i /> Kataloq</span><span><i /> QR analitika</span><span><i /> Sahə nəzarəti</span></div>
        <div className="login-grain" aria-hidden="true" />
        <div className="login-rings" aria-hidden="true"><span /><span /><b>WM</b></div>
      </section>

      <section className="login-panel">
        <form onSubmit={onSubmit} className="login-form">
          <div className="login-form-head">
            <img className="login-mobile-logo" src="/img/woodmark-brown.png"
                 alt="WOODMARK" width={52} height={52} />
            <span className="eyebrow">XOŞ GƏLMİSİNİZ</span>
            <h2>İdarəetmə panelinə giriş</h2>
            <p>İş mühitinə davam etmək üçün məlumatlarınızı daxil edin.</p>
          </div>

          <label className="login-field">
            <span>E-poçt</span>
            <span className="field-control">
              <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="5" width="18" height="14" rx="3" /><path d="m4 7 8 6 8-6" /></svg>
              <input type="email" required autoFocus autoComplete="username" value={email} onChange={event => setEmail(event.target.value)} placeholder="admin@qrcatalog.az" />
            </span>
          </label>

          <label className="login-field">
            <span>Parol</span>
            <span className="field-control">
              <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="10" width="16" height="11" rx="3" /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 15v2" /></svg>
              <input type="password" required autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} placeholder="••••••••" />
            </span>
          </label>

          <label className="login-remember">
            <input
              type="checkbox"
              checked={rememberMe}
              onChange={event => setRememberMe(event.target.checked)}
            />
            <span>Məni xatırla <small>30 gün — yalnız öz telefonunuzda işarələyin</small></span>
          </label>

          {login.isError && <p role="alert" className="login-error">{login.error.message}</p>}

          <button type="submit" disabled={login.isPending} className="login-submit">
            <span>{login.isPending ? 'Yoxlanılır…' : 'Daxil ol'}</span><b aria-hidden="true">→</b>
          </button>
          <p className="login-security"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z" /><path d="m9 12 2 2 4-4" /></svg> Təhlükəsiz və qorunan giriş</p>
        </form>
      </section>
    </main>
  )
}
