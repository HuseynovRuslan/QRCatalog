import { useEffect, useState, type FormEvent } from 'react'
import { useChangePassword } from '../api/auth'
import { useSaveSettings, useSettings } from '../api/settings'

export default function Settings() {
  const settings = useSettings()
  const save = useSaveSettings()

  const [name, setName] = useState('')
  const [phone, setPhone] = useState('')
  const [whatsapp, setWhatsapp] = useState('')

  useEffect(() => {
    if (settings.data) {
      setName(settings.data.name)
      setPhone(settings.data.phone ?? '')
      setWhatsapp(settings.data.whatsappNumber ?? '')
    }
  }, [settings.data])

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    save.mutate({ name, phone: phone || null, whatsappNumber: whatsapp || null })
  }

  return (
    <div className="admin-page settings-page max-w-lg">
      <h1 className="text-lg font-semibold tracking-tight">Parametrlər</h1>
      <p className="mt-1 text-sm text-stone-500">
        Əlaqə məlumatları public məhsul səhifəsindəki düymələrdə görünür.
      </p>

      <form onSubmit={onSubmit} className="mobile-form-card settings-form mt-6 space-y-4">
        <label className="block">
          <span className="text-sm font-medium text-stone-700">Müəssisə adı</span>
          <input required value={name} onChange={e => setName(e.target.value)}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700" />
        </label>

        <label className="block">
          <span className="text-sm font-medium text-stone-700">Telefon</span>
          <input value={phone} onChange={e => setPhone(e.target.value)}
            placeholder="+994 50 123 45 67"
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700" />
          <span className="mt-1 block text-xs text-stone-400">Boş qalsa «Zəng et» düyməsi görünmür.</span>
        </label>

        <label className="block">
          <span className="text-sm font-medium text-stone-700">WhatsApp nömrəsi</span>
          <input value={whatsapp} onChange={e => setWhatsapp(e.target.value)}
            placeholder="994501234567"
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 font-mono text-sm outline-none focus:border-emerald-700" />
          <span className="mt-1 block text-xs text-stone-400">
            Ölkə kodu ilə, yalnız rəqəm. Boş qalsa WhatsApp düyməsi görünmür.
          </span>
        </label>

        {save.isError && (
          <p role="alert" className="rounded bg-red-50 px-3 py-2 text-sm text-red-800">
            {save.error.message}
          </p>
        )}

        <button type="submit" disabled={save.isPending}
          className="rounded bg-emerald-800 px-5 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-60">
          {save.isPending ? 'Saxlanılır…' : 'Saxla'}
        </button>
        {save.isSuccess && !save.isPending && (
          <span className="ml-3 text-sm text-emerald-800">Saxlanıldı ✓</span>
        )}
      </form>

      <PasswordSection />
    </div>
  )
}

/* Müvəqqəti parolla girən işçi daimi parolunu BURADAN qoyur — SMTP olmadığı üçün
   "unutdum" axını yoxdur, bu isə minimum gigiyenadır. */
function PasswordSection() {
  const change = useChangePassword()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [repeat, setRepeat] = useState('')
  const mismatch = repeat.length > 0 && next !== repeat

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    if (mismatch) return
    change.mutate(
      { currentPassword: current, newPassword: next },
      { onSuccess: () => { setCurrent(''); setNext(''); setRepeat('') } },
    )
  }

  return (
    <form onSubmit={onSubmit} className="mobile-form-card settings-form mt-10 space-y-4">
      <div>
        <h2 className="text-base font-semibold tracking-tight">Parolu dəyiş</h2>
        <p className="mt-1 text-sm text-stone-500">
          Ən azı 8 simvol: böyük və kiçik hərf, rəqəm.
        </p>
      </div>

      <label className="block">
        <span className="text-sm font-medium text-stone-700">Hazırkı parol</span>
        <input type="password" required autoComplete="current-password" value={current}
          onChange={e => setCurrent(e.target.value)}
          className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700" />
      </label>

      <label className="block">
        <span className="text-sm font-medium text-stone-700">Yeni parol</span>
        <input type="password" required minLength={8} autoComplete="new-password" value={next}
          onChange={e => setNext(e.target.value)}
          className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700" />
      </label>

      <label className="block">
        <span className="text-sm font-medium text-stone-700">Yeni parol (təkrar)</span>
        <input type="password" required autoComplete="new-password" value={repeat}
          onChange={e => setRepeat(e.target.value)}
          className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700" />
        {mismatch && <span className="mt-1 block text-xs text-red-700">Parollar uyğun gəlmir.</span>}
      </label>

      {change.isError && (
        <p role="alert" className="rounded bg-red-50 px-3 py-2 text-sm text-red-800">
          {(change.error as Error).message}
        </p>
      )}

      <button type="submit" disabled={change.isPending || mismatch}
        className="rounded bg-emerald-800 px-5 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-60">
        {change.isPending ? 'Dəyişdirilir…' : 'Parolu dəyiş'}
      </button>
      {change.isSuccess && !change.isPending && (
        <span className="ml-3 text-sm text-emerald-800">Parol dəyişdirildi ✓</span>
      )}
    </form>
  )
}
