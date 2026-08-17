import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export interface QrCode {
  id: string
  token: string
  humanCode: string
  targetType: 'Product' | 'Category' | 'Archive'
  targetId: string | null
  status: 'Active' | 'Retired'
  createdAtUtc: string
  targetName: string | null
}

export interface PagedResult<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

const KEY = ['qrcodes']

export function useQrCodes(search: string, page: number) {
  const params = new URLSearchParams()
  if (search) params.set('search', search)
  params.set('page', String(page))
  return useQuery<PagedResult<QrCode>, Error>({
    queryKey: [...KEY, search, page],
    queryFn: () => api<PagedResult<QrCode>>(`/api/admin/qrcodes?${params}`),
  })
}

/** Bir hədəfin (məhsul/kateqoriya) kodları — məhsul səhifəsindəki QR bölməsi üçün. */
export function useQrCodesForTarget(targetId: string | undefined) {
  return useQuery<PagedResult<QrCode>, Error>({
    queryKey: [...KEY, 'target', targetId],
    queryFn: () => api<PagedResult<QrCode>>(`/api/admin/qrcodes?targetId=${targetId}&pageSize=50`),
    enabled: Boolean(targetId),
  })
}

function useInvalidating<TArgs>(fn: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useCreateQrCode() {
  return useInvalidating((input: { targetType: string; targetId: string | null }) =>
    api<QrCode>('/api/admin/qrcodes', { method: 'POST', body: JSON.stringify(input) }),
  )
}

export function useRetireQrCode() {
  return useInvalidating((id: string) =>
    api<void>(`/api/admin/qrcodes/${id}/retire`, { method: 'POST' }),
  )
}

export function useActivateQrCode() {
  return useInvalidating((id: string) =>
    api<void>(`/api/admin/qrcodes/${id}/activate`, { method: 'POST' }),
  )
}

/** PDF vərəqi blob kimi gəlir — fetch birbaşa, api() JSON gözləyir. */
export function useSheetDownload() {
  return useMutation({
    mutationFn: async (ids: string[]) => {
      const tokenRes = await fetch('/api/auth/antiforgery')
      const { token } = (await tokenRes.json()) as { token: string }
      const res = await fetch('/api/admin/qrcodes/sheet', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token },
        body: JSON.stringify({ ids }),
      })
      if (!res.ok) throw new Error(`Vərəq hazırlana bilmədi (${res.status}).`)
      const blob = await res.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      a.href = url
      a.download = 'qr-etiketler.pdf'
      a.click()
      URL.revokeObjectURL(url)
    },
  })
}
