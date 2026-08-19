import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export interface UserRow {
  id: string
  email: string
  displayName: string
  role: string
  deactivated: boolean
  hasCode: boolean
}

export const ROLE_OPTIONS = [
  { value: 'Admin', label: 'Admin', hint: 'Tam səlahiyyət — istifadəçilər daxil' },
  { value: 'Editor', label: 'Redaktor', hint: 'Məhsul, QR, obyekt idarə edir' },
  { value: 'Viewer', label: 'Baxış', hint: 'Yalnız oxuyur — sahə işçisi üçün' },
] as const

const KEY = ['users']

export function useUsers() {
  return useQuery<UserRow[], Error>({
    queryKey: KEY,
    queryFn: () => api<UserRow[]>('/api/admin/users'),
  })
}

function useInvalidating<TArgs, TResult>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useCreateUser() {
  return useInvalidating((input: { email: string; displayName: string; role: string }) =>
    api<{ id: string; email: string; tempPassword: string; accessCode: string }>('/api/admin/users', {
      method: 'POST',
      body: JSON.stringify(input),
    }),
  )
}

export function useSetUserRole() {
  return useInvalidating(({ id, role }: { id: string; role: string }) =>
    api<void>(`/api/admin/users/${id}/role`, { method: 'PUT', body: JSON.stringify({ role }) }),
  )
}

export function useDeactivateUser() {
  return useInvalidating((id: string) =>
    api<void>(`/api/admin/users/${id}/deactivate`, { method: 'POST' }),
  )
}

export function useActivateUser() {
  return useInvalidating((id: string) =>
    api<void>(`/api/admin/users/${id}/activate`, { method: 'POST' }),
  )
}

export function useResetUserPassword() {
  return useInvalidating((id: string) =>
    api<{ tempPassword: string }>(`/api/admin/users/${id}/reset-password`, { method: 'POST' }),
  )
}

/* Kod itəndə yenisi verilir — köhnəsi dərhal ölür və o kodla açılmış
   sessiyalar da bağlanır (ssenari «telefon itdi»dir). */
export function useResetUserCode() {
  return useInvalidating((id: string) =>
    api<{ accessCode: string }>(`/api/admin/users/${id}/reset-code`, { method: 'POST' }),
  )
}
