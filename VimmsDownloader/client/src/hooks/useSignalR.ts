import { useEffect, useState } from 'react'
import { HubConnectionBuilder, HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import type { DownloadAction } from './useDownloadState'
import type { Ps3IsoStatusEvent, SyncProgressEvent, SyncCompletedEvent } from '../types/signalr'
import { parseProgress } from '../lib/format'

export function useSignalR(dispatch: React.Dispatch<DownloadAction>) {
  const [connection, setConnection] = useState<HubConnection | null>(null)
  const queryClient = useQueryClient()

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hub')
      .withAutomaticReconnect()
      .build()

    connection.on('Status', (msg: string) => {
      dispatch({ type: 'STATUS', url: msg })
    })

    connection.on('Progress', (msg: string) => {
      const info = parseProgress(msg)
      if (info) dispatch({ type: 'PROGRESS', info })
    })

    connection.on('Completed', () => {
      dispatch({ type: 'COMPLETED' })
      queryClient.invalidateQueries({ queryKey: ['data'] })
    })

    connection.on('ConvertStatus', (data: Ps3IsoStatusEvent) => {
      dispatch({ type: 'CONV_STATUS', payload: data })
      if (data.phase === 'done' || data.phase === 'error') {
        queryClient.invalidateQueries({ queryKey: ['data'] })
      }
    })

    connection.on('Error', (msg: string) => {
      dispatch({ type: 'ERROR', message: msg })
    })

    connection.on('Done', () => {
      dispatch({ type: 'DONE' })
      queryClient.invalidateQueries({ queryKey: ['data'] })
    })

    connection.on('SyncProgress', (data: SyncProgressEvent) => {
      dispatch({ type: 'SYNC_PROGRESS', payload: data })
    })

    connection.on('SyncCompleted', (data: SyncCompletedEvent) => {
      dispatch({ type: 'SYNC_COMPLETED', filename: data.filename })
      queryClient.invalidateQueries({ queryKey: ['sync'] })
    })

    connection.onreconnecting(() => {
      dispatch({ type: 'CONNECTION', state: 'reconnecting' })
    })

    connection.onreconnected(() => {
      dispatch({ type: 'CONNECTION', state: 'connected' })
      queryClient.invalidateQueries()
    })

    connection.onclose(() => {
      dispatch({ type: 'CONNECTION', state: 'disconnected' })
    })

    connection.start().then(() => {
      dispatch({ type: 'CONNECTION', state: 'connected' })
      setConnection(connection)
    }).catch(() => {
      dispatch({ type: 'CONNECTION', state: 'disconnected' })
    })

    return () => {
      connection.stop()
    }
  }, [dispatch, queryClient])

  return connection
}
