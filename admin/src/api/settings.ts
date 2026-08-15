import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export interface Settings {
  name: string
  phone: string | null
  whatsappNumber: string | null
}

const KEY = ['settings']

export function useSettings() {
  return useQuery<Settings, Error>({
    queryKey: KEY,
    queryFn: () => api<Settings>('/api/admin/settings'),
  })
}

export function useSaveSettings() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: Settings) =>
      api<void>('/api/admin/settings', { method: 'PUT', body: JSON.stringify(input) }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}
