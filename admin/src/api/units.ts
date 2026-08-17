import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export type UnitStatus = 'Installed' | 'InStock' | 'InRepair' | 'Removed'

export interface Unit {
  id: string
  code: string
  productId: string
  productName: string
  siteId: string | null
  siteName: string | null
  /** Nüsxənin öz koordinatı yoxdursa obyektin koordinatı gəlir. */
  latitude: number | null
  longitude: number | null
  hasOwnPosition: boolean
  status: UnitStatus
  installedOn: string | null
  note: string | null
  updatedAtUtc: string
}

export const UNIT_STATUSES: { value: UnitStatus; label: string; color: string }[] = [
  { value: 'Installed', label: 'Quraşdırılıb', color: '#15803d' },
  { value: 'InStock', label: 'Anbarda', color: '#1d4ed8' },
  { value: 'InRepair', label: 'Təmirdə', color: '#b45309' },
  { value: 'Removed', label: 'Çıxarılıb', color: '#78716c' },
]

export interface UnitFilters {
  productId?: string
  siteId?: string
  status?: string
  search?: string
}

const KEY = ['units']

export function useUnits(filters: UnitFilters) {
  const params = new URLSearchParams()
  if (filters.productId) params.set('productId', filters.productId)
  if (filters.siteId) params.set('siteId', filters.siteId)
  if (filters.status) params.set('status', filters.status)
  if (filters.search) params.set('search', filters.search)
  return useQuery<Unit[], Error>({
    queryKey: [...KEY, filters],
    queryFn: () => api<Unit[]>(`/api/admin/units?${params}`),
  })
}

function useInvalidating<TArgs, TResult>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: KEY })
      // Obyekt siyahısındaki say vahidlərdən hesablanır — o da təzələnməlidir
      queryClient.invalidateQueries({ queryKey: ['sites'] })
    },
  })
}

export function useBulkCreateUnits() {
  return useInvalidating(
    (input: {
      productId: string
      siteId: string | null
      quantity: number
      installedOn: string | null
      note: string | null
      spreadMeters?: number
    }) =>
      api<{ count: number; codes: string[] }>('/api/admin/units/bulk', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
  )
}

export function useUpdateUnit() {
  return useInvalidating(
    ({
      id,
      ...input
    }: {
      id: string
      siteId: string | null
      latitude: number | null
      longitude: number | null
      installedOn: string | null
      status: UnitStatus
      note: string | null
    }) => api<void>(`/api/admin/units/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  )
}

export function useMoveUnit() {
  return useInvalidating(({ id, latitude, longitude }: { id: string; latitude: number; longitude: number }) =>
    api<void>(`/api/admin/units/${id}/position`, {
      method: 'PUT',
      body: JSON.stringify({ latitude, longitude }),
    }),
  )
}

export function useDeleteUnit() {
  return useInvalidating((id: string) =>
    api<void>(`/api/admin/units/${id}`, { method: 'DELETE' }),
  )
}
