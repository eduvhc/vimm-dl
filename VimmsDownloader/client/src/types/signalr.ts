export interface Ps3IsoStatusEvent {
  itemName: string
  phase: string
  message: string
  outputFilename?: string
  correlationId?: string
}

export interface SyncProgressEvent {
  filename: string
  percent: number
  copied: number
  total: number
}

export interface SyncCompletedEvent {
  filename: string
  success: boolean
  error?: string
}

export interface CompletedEvent {
  url: string
  filename: string
  filepath: string
}
