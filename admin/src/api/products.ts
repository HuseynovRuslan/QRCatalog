import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { PagedResult } from './qrcodes'

export interface ProductListItem {
  id: string
  name: string
  categoryName: string
  sku: string | null
  status: 'Draft' | 'Published' | 'Archived'
  updatedAtUtc: string
  thumbnailUrl: string | null
}

export interface SpecItem {
  label: string
  value: string
}

export interface ImageVariant {
  width: number
  url: string
}

export interface ProductImage {
  id: string
  altText: string | null
  variants: ImageVariant[]
}

export interface ProductDetail {
  id: string
  name: string
  description: string | null
  categoryId: string
  sku: string | null
  slug: string
  status: 'Draft' | 'Published' | 'Archived'
  specs: SpecItem[]
  images: ProductImage[]
}

export interface SaveProductInput {
  name: string
  description: string | null
  categoryId: string
  sku: string | null
}

const KEY = ['products']

export function useProducts(search: string, categoryId: string, status: string, page: number) {
  const params = new URLSearchParams()
  if (search) params.set('search', search)
  if (categoryId) params.set('categoryId', categoryId)
  if (status) params.set('status', status)
  params.set('page', String(page))
  return useQuery<PagedResult<ProductListItem>, Error>({
    queryKey: [...KEY, search, categoryId, status, page],
    queryFn: () => api<PagedResult<ProductListItem>>(`/api/admin/products?${params}`),
  })
}

export function useProduct(id: string | undefined) {
  return useQuery<ProductDetail, Error>({
    queryKey: [...KEY, 'detail', id],
    queryFn: () => api<ProductDetail>(`/api/admin/products/${id}`),
    enabled: !!id,
  })
}

function useInvalidating<TArgs, TResult = unknown>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useCreateProduct() {
  return useInvalidating((input: SaveProductInput) =>
    api<{ id: string }>('/api/admin/products', {
      method: 'POST',
      body: JSON.stringify(input),
    }),
  )
}

export function useUpdateProduct() {
  return useInvalidating(({ id, ...input }: SaveProductInput & { id: string }) =>
    api<void>(`/api/admin/products/${id}`, { method: 'PUT', body: JSON.stringify(input) }),
  )
}

export function useProductAction() {
  return useInvalidating(
    ({ id, action }: { id: string; action: 'publish' | 'unpublish' | 'archive' | 'copy' }) =>
      api<{ id: string } | void>(`/api/admin/products/${id}/${action}`, { method: 'POST' }),
  )
}

export function useReplaceSpecs() {
  return useInvalidating(({ id, specs }: { id: string; specs: SpecItem[] }) =>
    api<void>(`/api/admin/products/${id}/specs`, {
      method: 'PUT',
      body: JSON.stringify({ specs }),
    }),
  )
}

export function useUploadImages() {
  return useInvalidating(async ({ id, files }: { id: string; files: FileList }) => {
    const tokenRes = await fetch('/api/auth/antiforgery')
    const { token } = (await tokenRes.json()) as { token: string }
    const form = new FormData()
    for (const file of files) form.append('files', file)
    // Content-Type əl ilə verilmir — boundary-ni brauzer qoyur
    const res = await fetch(`/api/admin/products/${id}/images`, {
      method: 'POST',
      headers: { 'X-XSRF-TOKEN': token },
      body: form,
    })
    if (!res.ok) {
      let title = `Yükləmə alınmadı (${res.status}).`
      try {
        const problem = (await res.json()) as { title?: string }
        if (problem.title) title = problem.title
      } catch { /* default mesaj qalır */ }
      throw new Error(title)
    }
  })
}

export function useDeleteImage() {
  return useInvalidating(({ id, imageId }: { id: string; imageId: string }) =>
    api<void>(`/api/admin/products/${id}/images/${imageId}`, { method: 'DELETE' }),
  )
}

export function useReorderImages() {
  return useInvalidating(({ id, orderedIds }: { id: string; orderedIds: string[] }) =>
    api<void>(`/api/admin/products/${id}/images/reorder`, {
      method: 'PUT',
      body: JSON.stringify({ orderedIds }),
    }),
  )
}

export function useSetImageAlt() {
  return useInvalidating(
    ({ id, imageId, altText }: { id: string; imageId: string; altText: string | null }) =>
      api<void>(`/api/admin/products/${id}/images/${imageId}`, {
        method: 'PUT',
        body: JSON.stringify({ altText }),
      }),
  )
}
