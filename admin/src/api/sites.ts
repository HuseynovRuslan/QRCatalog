import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export type SiteKind =
  | 'Park'
  | 'Hotel'
  | 'Cafe'
  | 'School'
  | 'Residential'
  | 'Beach'
  | 'Other'

/** Obyektdə bir modelin yığımı — nüsxə (vahid) qeydlərindən hesablanır. */
export interface SiteItem {
  productId: string
  productName: string
  quantity: number
}

export interface Site {
  id: string
  name: string
  kind: SiteKind
  address: string | null
  latitude: number
  longitude: number
  contactName: string | null
  contactPhone: string | null
  note: string | null
  updatedAtUtc: string
  items: SiteItem[]
  totalQuantity: number
}

export interface SaveSite {
  name: string
  kind: SiteKind
  latitude: number
  longitude: number
  address: string | null
  contactName: string | null
  contactPhone: string | null
  note: string | null
}

export const SITE_KINDS: { value: SiteKind; label: string }[] = [
  { value: 'Park', label: 'Park' },
  { value: 'Hotel', label: 'Hotel' },
  { value: 'Cafe', label: 'Kafe / restoran' },
  { value: 'School', label: 'Məktəb / bağça' },
  { value: 'Residential', label: 'Yaşayış kompleksi' },
  { value: 'Beach', label: 'Çimərlik' },
  { value: 'Other', label: 'Digər' },
]

const KEY = ['sites']

export function useSites(search: string, productId = '') {
  const params = new URLSearchParams()
  if (search) params.set('search', search)
  if (productId) params.set('productId', productId)
  return useQuery<Site[], Error>({
    queryKey: [...KEY, search, productId],
    queryFn: () => api<Site[]>(`/api/admin/sites?${params}`),
  })
}

/** Bir modelin quraşdırıldığı yerlər — məhsul səhifəsindəki xəritə üçün. */
export function useSitesForProduct(productId: string | undefined) {
  return useQuery<Site[], Error>({
    queryKey: [...KEY, 'product', productId],
    queryFn: () => api<Site[]>(`/api/admin/sites?productId=${productId}`),
    enabled: Boolean(productId),
  })
}

// TResult saxlanılır: yeni obyekt yaradanda mutateAsync-in qaytardığı id lazımdır
function useInvalidating<TArgs, TResult>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useCreateSite() {
  return useInvalidating((input: SaveSite) =>
    api<{ id: string }>('/api/admin/sites', { method: 'POST', body: JSON.stringify(input) }),
  )
}

export function useUpdateSite() {
  return useInvalidating(({ id, ...input }: SaveSite & { id: string }) =>
    api<void>(`/api/admin/sites/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  )
}

export function useDeleteSite() {
  return useInvalidating((id: string) =>
    api<void>(`/api/admin/sites/${id}`, { method: 'DELETE' }),
  )
}
