import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { PagedResult } from './qrcodes'

export type InquiryStatus = 'New' | 'InProgress' | 'Answered' | 'Closed'

export interface Inquiry {
  id: string
  name: string
  phone: string
  message: string | null
  status: InquiryStatus
  internalNote: string | null
  productName: string | null
  humanCode: string | null
  createdAtUtc: string
}

const KEY = ['inquiries']

export function useInquiries(status: string, page: number) {
  const params = new URLSearchParams()
  if (status) params.set('status', status)
  params.set('page', String(page))
  return useQuery<PagedResult<Inquiry>, Error>({
    queryKey: [...KEY, status, page],
    queryFn: () => api<PagedResult<Inquiry>>(`/api/admin/inquiries?${params}`),
  })
}

function useInvalidating<TArgs>(fn: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useSetInquiryStatus() {
  return useInvalidating(({ id, status }: { id: string; status: InquiryStatus }) =>
    api<void>(`/api/admin/inquiries/${id}/status`, {
      method: 'PUT',
      body: JSON.stringify({ status }),
    }),
  )
}

export function useSetInquiryNote() {
  return useInvalidating(({ id, note }: { id: string; note: string | null }) =>
    api<void>(`/api/admin/inquiries/${id}/note`, {
      method: 'PUT',
      body: JSON.stringify({ note }),
    }),
  )
}
