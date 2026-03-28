import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type {
  DataResponse, VersionResponse, SettingsResponse, MetaResponse,
  QueueImportResponse, Ps3ConvertResponse, SyncCompareResponse, QueueExportItem,
  EventsResponse, MetricsResponse, AddResponse,
} from '../types/api'

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url)
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  return res.json()
}

async function postJson<T>(url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method: 'POST',
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : {},
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  if (!res.ok) throw new Error(await res.text())
  const text = await res.text()
  return text ? JSON.parse(text) : (undefined as T)
}

async function del(url: string): Promise<void> {
  const res = await fetch(url, { method: 'DELETE' })
  if (!res.ok) throw new Error(await res.text())
}

async function patch(url: string, body: unknown): Promise<void> {
  const res = await fetch(url, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await res.text())
}

// --- Data (merged: queue + history + status) ---

export function useData() {
  return useQuery({
    queryKey: ['data'],
    queryFn: () => fetchJson<DataResponse>('/api/data'),
    refetchInterval: 10000,
  })
}

// --- Settings (merged: config + settings) ---

export function useSettings() {
  return useQuery({
    queryKey: ['settings'],
    queryFn: () => fetchJson<SettingsResponse>('/api/settings'),
  })
}

export function useSaveSetting() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { key: string; value: string }) =>
      postJson('/api/settings', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] }),
  })
}

// --- Metadata ---

const ONE_HOUR = 60 * 60 * 1000

export function useVersion() {
  return useQuery({
    queryKey: ['version'],
    queryFn: () => fetchJson<VersionResponse>('/api/version'),
    staleTime: ONE_HOUR,
    refetchInterval: ONE_HOUR,
  })
}

export function useMeta(url: string | null) {
  return useQuery({
    queryKey: ['meta', url],
    queryFn: () => fetchJson<MetaResponse>(`/api/meta?url=${encodeURIComponent(url!)}`),
    enabled: !!url,
    staleTime: Infinity,
  })
}

// --- Queue ---

export function useAddToQueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { urls: string[]; format?: number; force?: boolean }) =>
      postJson<AddResponse>('/api/queue', data),
    onSuccess: (data) => {
      if (data?.queued) qc.invalidateQueries({ queryKey: ['data'] })
    },
  })
}

export function usePatchQueueItem() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { id: number; direction?: string; format?: number }) =>
      patch(`/api/queue/${data.id}`, { direction: data.direction, format: data.format }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useReorderQueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (ids: number[]) =>
      postJson('/api/queue/reorder', { ids }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useDeleteFromQueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => del(`/api/queue/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useClearQueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => del('/api/queue'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useDeleteCompleted() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, deleteFiles }: { id: number; deleteFiles?: boolean }) =>
      del(`/api/completed/${id}${deleteFiles ? '?deleteFiles=true' : ''}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export function useImportQueue() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (items: QueueExportItem[]) =>
      postJson<QueueImportResponse>('/api/queue/import', items),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

export async function exportQueue(): Promise<QueueExportItem[]> {
  return fetchJson('/api/queue/export')
}

// --- PS3 (merged endpoints) ---

export function useConvertPs3() {
  return useMutation({
    mutationFn: (filename?: string) =>
      postJson<Ps3ConvertResponse>('/api/ps3/convert', { filename: filename ?? null }),
  })
}

export function usePs3Action() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { filename: string; action: 'mark-done' | 'abort' }) =>
      postJson('/api/ps3/action', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['data'] }),
  })
}

// --- Sync (merged endpoints) ---

export function useSyncCompare() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (path: string) =>
      postJson<SyncCompareResponse>('/api/sync/compare', { path }),
    onSuccess: (data) => qc.setQueryData(['sync'], data),
  })
}

export function useSyncCopy() {
  return useMutation({
    mutationFn: (filename?: string) =>
      postJson('/api/sync/copy', { filename: filename ?? null }),
  })
}

export function useSyncCancel() {
  return useMutation({
    mutationFn: () => postJson('/api/sync/cancel'),
  })
}

// --- Events ---

export function useEvents(type?: string, item?: string, limit = 100) {
  const params = new URLSearchParams()
  params.set('limit', limit.toString())
  if (type) params.set('type', type)
  if (item) params.set('item', item)
  return useQuery({
    queryKey: ['events', type, item, limit],
    queryFn: () => fetchJson<EventsResponse>(`/api/events?${params}`),
    refetchInterval: 5000,
  })
}

// --- Metrics ---

export function useMetrics() {
  return useQuery({
    queryKey: ['metrics'],
    queryFn: () => fetchJson<MetricsResponse>('/api/metrics'),
    refetchInterval: 10000,
  })
}
