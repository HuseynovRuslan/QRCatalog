export class UnauthorizedError extends Error {
  constructor() {
    super('Sessiya bitib və ya giriş edilməyib.')
    this.name = 'UnauthorizedError'
  }
}

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
  ) {
    super(message)
    this.name = 'ApiError'
  }
}

let csrfToken: string | null = null

/** Girişdən sonra token rotasiya olunur — köhnəsini at. */
export function resetCsrfToken() {
  csrfToken = null
}

async function ensureCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken
  const res = await fetch('/api/auth/antiforgery')
  if (!res.ok) throw new ApiError('Antiforgery token alına bilmədi.', res.status)
  const body = (await res.json()) as { token: string }
  csrfToken = body.token
  return csrfToken
}

export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase()
  const headers = new Headers(options.headers)

  if (method !== 'GET' && method !== 'HEAD') {
    headers.set('X-XSRF-TOKEN', await ensureCsrfToken())
    if (options.body != null) headers.set('Content-Type', 'application/json')
  }

  const res = await fetch(path, { ...options, headers })

  if (res.status === 401) throw new UnauthorizedError()
  if (!res.ok) {
    let title = `Sorğu alınmadı (${res.status}).`
    try {
      const problem = (await res.json()) as { title?: string }
      if (problem.title) title = problem.title
    } catch {
      /* problem+json gəlməyibsə default mesaj qalır */
    }
    throw new ApiError(title, res.status)
  }

  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}
