import { expect, type APIRequestContext } from '@playwright/test'

export const ADMIN_EMAIL = process.env.E2E_ADMIN_EMAIL ?? 'admin@local.az'
export const ADMIN_PASSWORD = process.env.E2E_ADMIN_PASSWORD ?? 'Passw0rd!'

/** API üzərindən giriş + antiforgery — sonrakı API çağırışları üçün.
 *
 * DİQQƏT: `/api/auth/login` bir IP üçün dəqiqədə 10 girişlə məhdudlaşır, bütün dəst
 * isə ~5 giriş edir. Ona görə dəsti dalbadal iki dəfədən çox işlətmək anlaşılmaz
 * xətalar verir (giriş səhifəsində qalır) — bir dəqiqə gözləyin, kod səhv deyil. */
export async function apiLogin(request: APIRequestContext) {
  const tokenRes = await request.get('/api/auth/antiforgery')
  const { token } = (await tokenRes.json()) as { token: string }

  const login = await request.post('/api/auth/login', {
    headers: { 'X-XSRF-TOKEN': token },
    data: { email: ADMIN_EMAIL, password: ADMIN_PASSWORD },
  })
  expect(login.ok()).toBeTruthy()
}

export async function apiPost<T = { id: string }>(
  request: APIRequestContext, path: string, data: unknown,
): Promise<T> {
  const tokenRes = await request.get('/api/auth/antiforgery')
  const { token } = (await tokenRes.json()) as { token: string }
  const res = await request.post(path, { headers: { 'X-XSRF-TOKEN': token }, data })
  expect(res.ok(), `${path}: ${res.status()}`).toBeTruthy()
  const body = await res.text()
  return body ? (JSON.parse(body) as T) : (undefined as T)
}

export async function apiGet<T>(request: APIRequestContext, path: string): Promise<T> {
  const res = await request.get(path)
  expect(res.ok(), `${path}: ${res.status()}`).toBeTruthy()
  return (await res.json()) as T
}

export async function apiPut(request: APIRequestContext, path: string, data: unknown) {
  const tokenRes = await request.get('/api/auth/antiforgery')
  const { token } = (await tokenRes.json()) as { token: string }
  const res = await request.put(path, { headers: { 'X-XSRF-TOKEN': token }, data })
  expect(res.ok(), `${path}: ${res.status()}`).toBeTruthy()
}

/** Unikal ad — testlər təkrar işə salınanda toqquşmasın. */
export function unique(prefix: string) {
  return `${prefix} ${Date.now().toString(36)}`
}
