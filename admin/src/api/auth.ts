import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, resetCsrfToken, UnauthorizedError } from './client'

export interface UserInfo {
  email: string
  displayName: string
  roles: string[]
  companyId: string | null
}

export function useMe() {
  return useQuery<UserInfo, Error>({
    queryKey: ['me'],
    queryFn: () => api<UserInfo>('/api/auth/me'),
    retry: (failureCount, error) =>
      error instanceof UnauthorizedError ? false : failureCount < 2,
    staleTime: 5 * 60 * 1000,
  })
}

export function useLogin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (credentials: { email: string; password: string; rememberMe: boolean }) =>
      api<UserInfo>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify(credentials),
      }),
    onSuccess: user => {
      resetCsrfToken() // giriş sessiyanı dəyişir — token yenilənməlidir
      queryClient.setQueryData(['me'], user)
    },
  })
}

/* Tək sahəli işçi girişi: kod həm şəxsiyyət, həm sirrdir — e-poçt tələb olunmur.
   Sahə işçisi üçün iki sahə iki yazı səhvi deməkdir. */
export function useLoginWithCode() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { code: string; rememberMe: boolean }) =>
      api<UserInfo>('/api/auth/login-code', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
    onSuccess: user => {
      resetCsrfToken()
      queryClient.setQueryData(['me'], user)
    },
  })
}

export function useLogout() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => api<void>('/api/auth/logout', { method: 'POST' }),
    onSettled: () => {
      resetCsrfToken()
      queryClient.removeQueries({ queryKey: ['me'] })
    },
  })
}

export function useChangePassword() {
  return useMutation({
    mutationFn: (input: { currentPassword: string; newPassword: string }) =>
      api<void>('/api/auth/change-password', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
    // Parol dəyişəndə server sessiyanı yenidən imzalayır — CSRF tokeni köhnəlir
    onSuccess: () => resetCsrfToken(),
  })
}
