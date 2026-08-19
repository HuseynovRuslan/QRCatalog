import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useLogin, useLoginWithCode } from '../api/auth'

/* Yalnız ÖZ saytımızın yolu qəbul edilir: kənar ünvan (`https://...`, `//evil.az`)
   parol oğurlama yönləndirməsinə çevrilə bilərdi. */
function safeReturnPath(raw: string | null): string | null {
  if (!raw) return null
  if (raw[0] !== '/' || raw[1] === '/' || raw[1] === String.fromCharCode(92)) return null
  return raw
}

/* Kodu oxunaqlı saxlayır: işçi "wm4k7p9x3m" yazsa da server normallaşdırır, amma
   ekranda defisli görünsün deyə yazıldıqca formatlanır. */
function formatCode(raw: string): string {
  const clean = raw.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 10)
  if (clean.length <= 2) return clean
  if (clean.length <= 6) return `${clean.slice(0, 2)}-${clean.slice(2)}`
  return `${clean.slice(0, 2)}-${clean.slice(2, 6)}-${clean.slice(6)}`
}

export default function Login() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [rememberMe, setRememberMe] = useState(false)
  // Kod ünvanda gələ bilər (işçiyə göndərilən link) — sahə hazır dolur, amma
  // AVTOMATİK göndərilmir: linki səhvən paylaşan adam kiminsə hesabına
  // xəbərsiz giriş etmiş olmasın.
  const [code, setCode] = useState(() =>
    formatCode(new URLSearchParams(window.location.search).get('kod') ?? ''))
  // İşçi girişi əsas yoldur; e-poçt+parol admin üçün qalır
  const [mode, setMode] = useState<'code' | 'email'>('code')
  const login = useLogin()
  const loginWithCode = useLoginWithCode()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'
  // Skamyadaki QR → müştəri səhifəsi → "İşçi girişi" → bura. Girişdən sonra HƏMİN
  // səhifəyə qayıdır; artıq girişli olduğu üçün Q səhifəsi admin ekranına yönləndirir.
  const returnPath = safeReturnPath(new URLSearchParams(window.location.search).get('qayit'))

  function goAfterLogin() {
    if (returnPath && !returnPath.startsWith('/admin')) {
      // SPA-dan kənar səhifə (məs. /q/token) — tam yüklənmə lazımdır
      window.location.assign(returnPath)
      return
    }
    navigate(returnPath ? returnPath.replace(/^\/admin/, '') || '/' : from, { replace: true })
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (mode === 'code') {
      loginWithCode.mutate({ code, rememberMe }, { onSuccess: goAfterLogin })
      return
    }
    login.mutate({ email, password, rememberMe }, { onSuccess: goAfterLogin })
  }

  const active = mode === 'code' ? loginWithCode : login

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
            <p>
              {mode === 'code'
                ? 'Sizə verilən işçi kodunu yazın — başqa heç nə lazım deyil.'
                : 'İş mühitinə davam etmək üçün məlumatlarınızı daxil edin.'}
            </p>
          </div>

          {mode === 'code' ? (
            <label className="login-field">
              <span>İşçi kodu</span>
              <span className="field-control">
                <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="10" width="16" height="11" rx="3" /><path d="M8 10V7a4 4 0 0 1 8 0v3" /></svg>
                <input
                  className="login-code-input"
                  required
                  autoFocus
                  autoCapitalize="characters"
                  autoCorrect="off"
                  spellCheck={false}
                  autoComplete="off"
                  value={code}
                  onChange={event => setCode(formatCode(event.target.value))}
                  placeholder="WM-XXXX-XXXX"
                />
              </span>
            </label>
          ) : (
            <>
              <label className="login-field">
                <span>E-poçt</span>
                <span className="field-control">
                  <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="5" width="18" height="14" rx="3" /><path d="m4 7 8 6 8-6" /></svg>
                  <input type="email" required autoFocus autoComplete="username" value={email} onChange={event => setEmail(event.target.value)} placeholder="ad@sirket.az" />
                </span>
              </label>

              <label className="login-field">
                <span>Parol</span>
                <span className="field-control">
                  <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="10" width="16" height="11" rx="3" /><path d="M8 10V7a4 4 0 0 1 8 0v3M12 15v2" /></svg>
                  <input type="password" required autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} placeholder="••••••••" />
                </span>
              </label>
            </>
          )}

          <label className="login-remember">
            <input
              type="checkbox"
              checked={rememberMe}
              onChange={event => setRememberMe(event.target.checked)}
            />
            <span>Məni xatırla <small>30 gün — yalnız öz telefonunuzda işarələyin</small></span>
          </label>

          {active.isError && <p role="alert" className="login-error">{active.error.message}</p>}

          <button type="submit" disabled={active.isPending} className="login-submit">
            <span>{active.isPending ? 'Yoxlanılır…' : 'Daxil ol'}</span><b aria-hidden="true">→</b>
          </button>

          <button
            type="button"
            className="login-mode-switch"
            onClick={() => setMode(mode === 'code' ? 'email' : 'code')}
          >
            {mode === 'code' ? 'E-poçt və parolla giriş' : 'İşçi kodu ilə giriş'}
          </button>
          <p className="login-security"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z" /><path d="m9 12 2 2 4-4" /></svg> Təhlükəsiz və qorunan giriş</p>
        </form>
      </section>
    </main>
  )
}
