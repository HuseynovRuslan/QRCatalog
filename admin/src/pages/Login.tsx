import { useState, type FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useLogin } from '../api/auth'

export default function Login() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const login = useLogin()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    login.mutate(
      { email, password },
      { onSuccess: () => navigate(from, { replace: true }) },
    )
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-stone-100 px-4">
      <form
        onSubmit={onSubmit}
        className="w-full max-w-sm rounded-lg border border-stone-200 bg-white p-8 shadow-sm"
      >
        <h1 className="text-xl font-semibold tracking-tight text-stone-900">QrCatalog</h1>
        <p className="mt-1 text-sm text-stone-500">İdarəetmə panelinə giriş</p>

        <label className="mt-6 block">
          <span className="text-sm font-medium text-stone-700">E-poçt</span>
          <input
            type="email"
            required
            autoComplete="username"
            value={email}
            onChange={e => setEmail(e.target.value)}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700 focus:ring-2 focus:ring-emerald-700/20"
          />
        </label>

        <label className="mt-4 block">
          <span className="text-sm font-medium text-stone-700">Parol</span>
          <input
            type="password"
            required
            autoComplete="current-password"
            value={password}
            onChange={e => setPassword(e.target.value)}
            className="mt-1 w-full rounded border border-stone-300 px-3 py-2 text-sm outline-none focus:border-emerald-700 focus:ring-2 focus:ring-emerald-700/20"
          />
        </label>

        {login.isError && (
          <p role="alert" className="mt-4 rounded bg-red-50 px-3 py-2 text-sm text-red-800">
            {login.error.message}
          </p>
        )}

        <button
          type="submit"
          disabled={login.isPending}
          className="mt-6 w-full rounded bg-emerald-800 px-4 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-60"
        >
          {login.isPending ? 'Yoxlanılır…' : 'Daxil ol'}
        </button>
      </form>
    </main>
  )
}
