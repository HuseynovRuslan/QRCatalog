import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'

export interface Category {
  id: string
  parentId: string | null
  name: string
  slug: string
  description: string | null
  codePrefix: string | null
  sortOrder: number
}

export interface CategoryInput {
  name: string
  parentId?: string | null
  description?: string | null
  codePrefix?: string | null
}

const KEY = ['categories']

export function useCategories() {
  return useQuery<Category[], Error>({
    queryKey: KEY,
    queryFn: () => api<Category[]>('/api/admin/categories'),
  })
}

function useInvalidating<TArgs>(fn: (args: TArgs) => Promise<unknown>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: fn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: KEY }),
  })
}

export function useCreateCategory() {
  return useInvalidating((input: CategoryInput) =>
    api<Category>('/api/admin/categories', {
      method: 'POST',
      body: JSON.stringify(input),
    }),
  )
}

export function useUpdateCategory() {
  return useInvalidating(
    ({ id, ...input }: { id: string; name: string; description?: string | null; codePrefix?: string | null }) =>
      api<void>(`/api/admin/categories/${id}`, {
        method: 'PUT',
        body: JSON.stringify(input),
      }),
  )
}

export function useReorderCategories() {
  return useInvalidating((orderedIds: string[]) =>
    api<void>('/api/admin/categories/reorder', {
      method: 'PUT',
      body: JSON.stringify({ orderedIds }),
    }),
  )
}

export function useDeleteCategory() {
  return useInvalidating((id: string) =>
    api<void>(`/api/admin/categories/${id}`, { method: 'DELETE' }),
  )
}
