import { useEffect, useState, type FormEvent } from 'react'
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
    </div>
  )
}
