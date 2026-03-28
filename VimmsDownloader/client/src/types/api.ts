export interface DataResponse {
  queued: QueuedItem[]
  history: HistoryItem[]
  isRunning: boolean
  isPaused: boolean
  currentFile: string | null
  currentUrl: string | null
  progress: string | null
  totalBytes: number
  downloadedBytes: number
}

export interface QueuedItem {
  id: number
  url: string
  format: number
  title: string | null
  platform: string | null
  size: string | null
  formats: string | null
}

export interface FormatOption {
  value: number
  label: string
  title: string
  size: string
}

export interface TraceStep {
  name: string
  status: 'pending' | 'active' | 'done' | 'error' | 'skipped'
  message: string | null
  durationMs: number | null
}

export interface PipelineTrace {
  pipelineType: string
  steps: TraceStep[]
  isoFilename: string | null
  isoSize: number | null
  actions: string[]
}

export interface HistoryItem {
  id: number
  url: string
  filename: string
  filepath: string | null
  title: string | null
  platform: string | null
  size: string | null
  fileExists: boolean
  fileSize: number | null
  trace: PipelineTrace | null
  completedAt: string | null
  format: number | null
}

export interface MetaResponse {
  title: string
  platform: string
  size: string
  formats: string | null
  serial: string | null
}

export interface VersionResponse {
  current: string
  latest: string | null
  hasUpdate: boolean
  url: string | null
  changelog: string | null
}

export interface AddResponse {
  queued: { id: number; url: string; format: number }[] | null
  duplicates: DuplicateInfo[] | null
}

export interface DuplicateInfo {
  url: string
  source: 'queued' | 'completed'
  reason: string
  title: string | null
  filename: string | null
  isoFilename: string | null
  archiveExists: boolean
  isoExists: boolean
}

export interface QueueExportItem {
  url: string
  format: number
}

export interface QueueImportResponse {
  added: number
  skipped: number
}

// Merged: config + settings
export interface SettingsResponse {
  platform: string
  osDescription: string
  hostname: string
  user: string
  defaultPath: string
  activePath: string
  fixThe: boolean
  addSerial: boolean
  stripRegion: boolean
  ipv4: string
  ps3Parallelism: number
  ps3DefaultFormat: number
  ps3PreserveArchive: boolean
  featureSync: boolean
  featureEvents: boolean
}

export interface CheckPathResponse {
  path: string | null
  exists: boolean
  writable: boolean
  freeSpace: number | null
  error: string | null
}

export interface Ps3ConvertResponse {
  queued: number
  skipped: number
  files: string[]
}

export interface SyncCompareResponse {
  new: SyncFileInfo[]
  synced: SyncFileInfo[]
  targetOnly: SyncFileInfo[]
  source: SyncDiskInfo | null
  target: SyncDiskInfo | null
  error: string | null
}

export interface SyncDiskInfo {
  label: string
  isoCount: number
  isoTotalSize: number
  freeSpace: number
  totalSpace: number
}

export interface SyncFileInfo {
  name: string
  size: number
}

export interface MetricsResponse {
  diskFreeBytes: number
  diskTotalBytes: number
  queuedTotalBytes: number
  queuedCount: number
  completedTotalBytes: number
  completedCount: number
  orphanedTotalBytes: number
  orphanedCount: number
  downloadingTotalBytes: number
  downloadingCount: number
}

export interface EventRow {
  id: number
  itemName: string
  eventType: string
  phase: string | null
  message: string | null
  data: string | null
  timestamp: string
  correlationId: string | null
}

export interface EventsResponse {
  events: EventRow[]
  total: number
}
