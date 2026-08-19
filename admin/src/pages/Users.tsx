import { useState, type FormEvent } from 'react'
import { useMe } from '../api/auth'
import {
  ROLE_OPTIONS,
  useActivateUser,
  useCreateUser,
  useDeactivateUser,
  useResetUserCode,
  useResetUserPassword,
  useSetUserRole,
  useUsers,
  type UserRow,
} from '../api/users'

/* Giriş məlumatları YALNIZ bir dəfə göstərilir — serverdə saxlanılmır (koddan
   yalnız hash qalır). Kod əsas yoldur: işçi telefonda tək sahə doldurur.
   Parol ikinci dərəcəlidir — kompüterdən e-poçtla girmək istəyən üçün. */
function CopyBox({ label, value, hint, big }: {
  label: string
  value: string
  hint?: string
  big?: boolean
}) {
  const [copied, setCopied] = useState(false)

  return (
    <div className="mt-3">
      <p className="text-xs font-semibold uppercase tracking-wide text-amber-800">{label}</p>
      <div className="mt-1 flex flex-wrap items-center gap-2">
        <code
          className={
            big
              ? 'rounded bg-white px-3 py-2 font-mono text-lg font-bold tracking-widest'
              : 'rounded bg-white px-3 py-1.5 font-mono text-sm break-all'
          }
        >
          {value}
        </code>
        <button
          type="button"
          onClick={() => {
            navigator.clipboard?.writeText(value).then(() => setCopied(true))
          }}
          className="rounded border border-amber-400 px-3 py-1.5 text-sm hover:bg-amber-100"
        >
          {copied ? 'Kopyalandı ✓' : 'Kopyala'}
        </button>
      </div>
      {hint && <p className="mt-1 text-xs text-amber-800">{hint}</p>}
    </div>
  )
}

function CredentialsNotice({ email, accessCode, tempPassword, onClose }: {
  email: string
  accessCode?: string
  tempPassword?: string
  onClose: () => void
}) {
  return (
    <div className="mt-4 rounded-lg border border-amber-300 bg-amber-50 p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <p className="text-sm font-semibold text-amber-900">
          {email} — giriş məlumatları YALNIZ İNDİ görünür
        </p>
        <button
          type="button"
          onClick={onClose}
          className="rounded px-3 py-1 text-sm text-amber-800 hover:bg-amber-100"
        >
          Bağla
        </button>
      </div>

      {accessCode && (
        <>
          <CopyBox
            big
            label="İşçi kodu — telefonda bununla girir"
            value={accessCode}
            hint="katalog.qrlog.az/admin → kodu yazır → «Məni xatırla» → 30 gün parol görmür."
          />
          <CopyBox
            label="Hazır link (WhatsApp ilə göndərmək üçün)"
            value={`https://katalog.qrlog.az/admin/login?kod=${accessCode}`}
            hint="Linki YALNIZ həmin işçiyə göndərin: açan adamda kod hazır dolur, «Daxil ol»a basmaq qalır."
          />
        </>
      )}

      {tempPassword && (
        <CopyBox
          label="Müvəqqəti parol (kompüterdən e-poçtla giriş üçün)"
          value={tempPassword}
          hint="İşçi ilk girişdən sonra Parametrlərdən dəyişməlidir."
        />
      )}

      <p className="mt-3 text-xs text-amber-800">
        Bu pəncərə bağlananda məlumatlar bir daha göstərilmir — itsə, yenisini yaradın.
      </p>
    </div>
  )
}

export default function Users() {
  const me = useMe()
  const users = useUsers()
  const create = useCreateUser()
  const setRole = useSetUserRole()
  const deactivate = useDeactivateUser()
  const activate = useActivateUser()
  const resetPassword = useResetUserPassword()
  const resetCode = useResetUserCode()

  const [adding, setAdding] = useState(false)
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [role, setRoleValue] = useState('Viewer')
  const [issued, setIssued] = useState<
    { email: string; accessCode?: string; tempPassword?: string } | null>(null)

  const rows = users.data ?? []
  const activeAdmins = rows.filter(u => u.role === 'Admin' && !u.deactivated).length
  const busy = create.isPending || setRole.isPending || deactivate.isPending ||
    activate.isPending || resetPassword.isPending || resetCode.isPending

  function onCreate(event: FormEvent) {
    event.preventDefault()
    create.mutate(
      { email, displayName, role },
      {
        onSuccess: result => {
          setIssued({
            email: result.email,
            accessCode: result.accessCode,
            tempPassword: result.tempPassword,
          })
          setAdding(false)
          setEmail('')
          setDisplayName('')
          setRoleValue('Viewer')
        },
      },
    )
  }

  return (
    <div className="admin-page users-page">
      <div className="page-heading flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold tracking-tight">İstifadəçilər</h1>
          <p className="mt-1 text-sm text-stone-500">
            Hər işçinin öz hesabı — audit jurnalı kimin nə etdiyini yalnız belə göstərə bilir.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setAdding(!adding)}
          className="page-primary-action rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700"
        >
          {adding ? 'Bağla' : 'Yeni istifadəçi'}
        </button>
      </div>

      {issued && (
        <CredentialsNotice
          email={issued.email}
          accessCode={issued.accessCode}
          tempPassword={issued.tempPassword}
          onClose={() => setIssued(null)}
        />
      )}

      {adding && (
        <form onSubmit={onCreate} className="mobile-form-card mt-4 rounded-lg border border-stone-200 bg-white p-4">
          <div className="grid gap-3 sm:grid-cols-3">
            <label className="block">
              <span className="text-sm font-medium text-stone-700">E-poçt</span>
              <input
                type="email"
                required
                value={email}
                onChange={e => setEmail(e.target.value)}
                placeholder="isci@woodmark.az"
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
              />
            </label>
            <label className="block">
              <span className="text-sm font-medium text-stone-700">Ad</span>
              <input
                value={displayName}
                onChange={e => setDisplayName(e.target.value)}
                maxLength={200}
                placeholder="Ad Soyad"
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
              />
            </label>
            <label className="block">
              <span className="text-sm font-medium text-stone-700">Rol</span>
              <select
                value={role}
                onChange={e => setRoleValue(e.target.value)}
                className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700"
              >
                {ROLE_OPTIONS.map(option => (
                  <option key={option.value} value={option.value}>
                    {option.label} — {option.hint}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <button
            type="submit"
            disabled={create.isPending}
            className="mt-4 rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-50"
          >
            {create.isPending ? 'Yaradılır…' : 'Yarat — işçi kodu veriləcək'}
          </button>
          {create.isError && (
            <p className="mt-2 text-sm text-red-700">{(create.error as Error).message}</p>
          )}
        </form>
      )}

      <div className="responsive-table users-table mt-4 overflow-x-auto rounded-lg border border-stone-200 bg-white">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-stone-200 text-left text-xs uppercase tracking-wide text-stone-400">
              <th className="px-3 py-2">İstifadəçi</th>
              <th className="px-3 py-2">Rol</th>
              <th className="px-3 py-2">Vəziyyət</th>
              <th className="px-3 py-2"></th>
            </tr>
          </thead>
          <tbody>
            {rows.map(user => (
              <UserRowView
                key={user.id}
                user={user}
                isSelf={user.email === me.data?.email}
                lastAdmin={user.role === 'Admin' && !user.deactivated && activeAdmins <= 1}
                busy={busy}
                onRole={newRole => setRole.mutate({ id: user.id, role: newRole })}
                onDeactivate={() => {
                  if (confirm(`${user.email} deaktiv edilsin? Mövcud sessiyaları 5 dəqiqə içində düşəcək.`))
                    deactivate.mutate(user.id)
                }}
                onActivate={() => activate.mutate(user.id)}
                onResetPassword={() => {
                  if (confirm(`${user.email} üçün yeni müvəqqəti parol yaradılsın? Köhnə parol və sessiyalar ləğv olunacaq.`))
                    resetPassword.mutate(user.id, {
                      onSuccess: result =>
                        setIssued({ email: user.email, tempPassword: result.tempPassword }),
                    })
                }}
                onResetCode={() => {
                  if (confirm(`${user.email} üçün yeni işçi kodu yaradılsın? Köhnə kod dərhal işləməyəcək.`))
                    resetCode.mutate(user.id, {
                      onSuccess: result =>
                        setIssued({ email: user.email, accessCode: result.accessCode }),
                    })
                }}
              />
            ))}
            {rows.length === 0 && !users.isPending && (
              <tr>
                <td colSpan={4} className="px-3 py-6 text-center text-stone-400">
                  İstifadəçi yoxdur.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {(setRole.isError || deactivate.isError || resetPassword.isError || resetCode.isError) && (
        <p className="mt-3 text-sm text-red-700">
          {((setRole.error ?? deactivate.error ?? resetPassword.error ?? resetCode.error) as Error).message}
        </p>
      )}
    </div>
  )
}

function UserRowView({
  user, isSelf, lastAdmin, busy, onRole, onDeactivate, onActivate, onResetPassword, onResetCode,
}: {
  user: UserRow
  isSelf: boolean
  lastAdmin: boolean
  busy: boolean
  onRole: (role: string) => void
  onDeactivate: () => void
  onActivate: () => void
  onResetPassword: () => void
  onResetCode: () => void
}) {
  return (
    <tr className="border-b border-stone-100 last:border-0 hover:bg-stone-50">
      <td data-label="İstifadəçi" className="px-3 py-2">
        <span className="font-medium">{user.displayName || user.email}</span>
        <span className="block text-xs text-stone-400">
          {user.email}
          {isSelf && ' · siz'}
        </span>
      </td>
      <td data-label="Rol" className="px-3 py-2">
        {/* Öz rolunu, ya son adminin rolunu dəyişmək server tərəfdə də qadağandır —
            burada sadəcə cəhdin qarşısı alınır ki, səhv mesajı ilə üzləşməsin */}
        <select
          value={user.role}
          disabled={busy || isSelf || lastAdmin}
          onChange={e => onRole(e.target.value)}
          className="rounded border border-stone-200 px-2 py-1 text-xs outline-none focus:border-emerald-700 disabled:opacity-60"
        >
          {ROLE_OPTIONS.map(option => (
            <option key={option.value} value={option.value}>{option.label}</option>
          ))}
        </select>
      </td>
      <td data-label="Vəziyyət" className="px-3 py-2">
        {user.deactivated ? (
          <span className="rounded-full bg-stone-100 px-2 py-0.5 text-xs font-medium text-stone-500">
            Deaktiv
          </span>
        ) : (
          <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-xs font-medium text-emerald-900">
            Aktiv
          </span>
        )}
      </td>
      <td className="px-3 py-2 text-right whitespace-nowrap" data-detail="true">
        <button
          type="button"
          disabled={busy}
          onClick={onResetCode}
          title={user.hasCode ? 'Yeni kod verilir, köhnəsi ölür' : 'Bu hesabda kod yoxdur — yaradın'}
          className="rounded px-2 py-1 text-xs text-emerald-800 hover:bg-emerald-50"
        >
          {user.hasCode ? 'Kodu yenilə' : 'Kod ver'}
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={onResetPassword}
          className="rounded px-2 py-1 text-xs text-stone-500 hover:bg-stone-100"
        >
          Parolu sıfırla
        </button>
        {user.deactivated ? (
          <button
            type="button"
            disabled={busy}
            onClick={onActivate}
            className="rounded px-2 py-1 text-xs text-emerald-800 hover:bg-emerald-50"
          >
            Aktivləşdir
          </button>
        ) : (
          <button
            type="button"
            disabled={busy || isSelf || lastAdmin}
            title={isSelf ? 'Öz hesabınızı deaktiv edə bilməzsiniz' : lastAdmin ? 'Son admin deaktiv edilə bilməz' : undefined}
            onClick={onDeactivate}
            className="rounded px-2 py-1 text-xs text-stone-500 hover:bg-stone-100 disabled:opacity-40"
          >
            Deaktiv et
          </button>
        )}
      </td>
    </tr>
  )
}
