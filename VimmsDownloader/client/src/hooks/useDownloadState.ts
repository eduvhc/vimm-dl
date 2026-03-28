import { createContext, useContext } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import type { Ps3IsoStatusEvent, SyncProgressEvent } from '../types/signalr'
import type { ParsedProgress } from '../lib/format'

export type ConnectionState = 'connected' | 'reconnecting' | 'disconnected'

export interface DownloadState {
  running: boolean
  paused: boolean
  activeUrl: string | null
  activeDlInfo: ParsedProgress | null
  convStatuses: Record<string, Ps3IsoStatusEvent>
  convStartTimes: Record<string, number>
  syncCopying: Record<string, SyncProgressEvent>
  connectionState: ConnectionState
  errors: string[]
}

export const initialState: DownloadState = {
  running: false,
  paused: false,
  activeUrl: null,
  activeDlInfo: null,
  convStatuses: {},
  convStartTimes: {},
  syncCopying: {},
  connectionState: 'disconnected',
  errors: [],
}

export type DownloadAction =
  | { type: 'SET_RUNNING'; running: boolean; paused: boolean }
  | { type: 'STATUS'; url: string }
  | { type: 'PROGRESS'; info: ParsedProgress }
  | { type: 'COMPLETED' }
  | { type: 'CONV_STATUS'; payload: Ps3IsoStatusEvent }
  | { type: 'SYNC_PROGRESS'; payload: SyncProgressEvent }
  | { type: 'SYNC_COMPLETED'; filename: string }
  | { type: 'ERROR'; message: string }
  | { type: 'CLEAR_ERRORS' }
  | { type: 'DONE' }
  | { type: 'CONNECTION'; state: ConnectionState }

export function downloadReducer(state: DownloadState, action: DownloadAction): DownloadState {
  switch (action.type) {
    case 'SET_RUNNING':
      return { ...state, running: action.running, paused: action.paused }

    case 'STATUS': {
      const m = action.url.match(/Processing:\s*(https?:\/\/\S+)/i)
      if (m) return { ...state, activeUrl: m[1], running: true, paused: false }
      return state
    }

    case 'PROGRESS':
      return { ...state, activeDlInfo: action.info, running: true, paused: false }

    case 'COMPLETED':
      return { ...state, activeUrl: null, activeDlInfo: null }

    case 'CONV_STATUS': {
      const next = { ...state.convStatuses, [action.payload.itemName]: action.payload }
      const prev = state.convStatuses[action.payload.itemName]
      const starts = { ...state.convStartTimes }
      // Track phase start time for elapsed display
      if (!prev || prev.phase !== action.payload.phase)
        starts[action.payload.itemName] = Date.now()
      return { ...state, convStatuses: next, convStartTimes: starts }
    }

    case 'SYNC_PROGRESS': {
      const next = { ...state.syncCopying, [action.payload.filename]: action.payload }
      return { ...state, syncCopying: next }
    }

    case 'SYNC_COMPLETED': {
      const next = { ...state.syncCopying }
      delete next[action.filename]
      return { ...state, syncCopying: next }
    }

    case 'ERROR':
      return { ...state, errors: [...state.errors.slice(-49), action.message] }

    case 'CLEAR_ERRORS':
      return { ...state, errors: [] }

    case 'DONE':
      return {
        ...state,
        running: false,
        paused: false,
        activeUrl: null,
        activeDlInfo: null,
      }

    case 'CONNECTION':
      return { ...state, connectionState: action.state }

    default:
      return state
  }
}

interface DownloadContextValue {
  state: DownloadState
  dispatch: React.Dispatch<DownloadAction>
  connection: HubConnection | null
}

export const DownloadContext = createContext<DownloadContextValue>({
  state: initialState,
  dispatch: () => {},
  connection: null,
})

export function useDownload() {
  return useContext(DownloadContext)
}
