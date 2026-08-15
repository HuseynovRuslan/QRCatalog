import { useQuery } from '@tanstack/react-query'
import { api } from './client'

export interface TopProduct {
  name: string
  count: number
}

export interface Dashboard {
  productsTotal: number
  productsPublished: number
  newInquiries: number
  scans30d: number
  topProducts: TopProduct[]
  unscannedCodes: number
}

export interface DayCount {
  date: string
  count: number
}

export interface CodeCount {
  humanCode: string
  productName: string | null
  count: number
}

export interface UnscannedCode {
  humanCode: string
  createdAtUtc: string
}

export interface ScanReport {
  byDay: DayCount[]
  byCode: CodeCount[]
  unscanned: UnscannedCode[]
}

export function useDashboard() {
  return useQuery<Dashboard, Error>({
    queryKey: ['stats', 'dashboard'],
    queryFn: () => api<Dashboard>('/api/admin/stats/dashboard'),
    refetchInterval: 60_000,
  })
}

export function useScanReport(days: number) {
  return useQuery<ScanReport, Error>({
    queryKey: ['stats', 'scans', days],
    queryFn: () => api<ScanReport>(`/api/admin/stats/scans?days=${days}`),
  })
}
