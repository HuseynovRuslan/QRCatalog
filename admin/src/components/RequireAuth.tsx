import { Navigate, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useMe } from '../api/auth'
import { UnauthorizedError } from '../api/client'

export default function RequireAuth({ children }: { children: ReactNode }) {
  const me = useMe()
  const location = useLocation()

  if (me.isPending) {
    return (
      <main className="flex min-h-screen items-center justify-center text-sm text-stone-500">
        Yüklənir…
      </main>
    )
  }

  if (me.isError) {
    if (me.error instanceof UnauthorizedError) {
      return <Navigate to="/login" replace state={{ from: location.pathname }} />
    }
    return (
      <main className="flex min-h-screen items-center justify-center px-4">
        <p role="alert" className="rounded bg-red-50 px-4 py-3 text-sm text-red-800">
          Server əlçatan deyil — bir azdan yenidən cəhd edin.
        </p>
      </main>
    )
  }

  return children
}
