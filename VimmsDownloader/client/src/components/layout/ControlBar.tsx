import { useDownload } from '../../hooks/useDownloadState'
import { useSettings, useClearQueue } from '../../api/queries'

export function ControlBar() {
  const { state, dispatch, connection } = useDownload()
  const { data: config } = useSettings()
  const clearMutation = useClearQueue()

  function handleStart() {
    if (!connection) return
    dispatch({ type: 'SET_RUNNING', running: true, paused: false })
    connection.invoke('StartDownload', config?.activePath ?? null)
  }

  function handlePause() {
    if (!connection) return
    dispatch({ type: 'SET_RUNNING', running: false, paused: true })
    connection.invoke('PauseDownload')
  }

  function handleStop() {
    if (!connection) return
    dispatch({ type: 'DONE' })
    connection.invoke('StopDownload')
  }

  function handleClear() {
    clearMutation.mutate()
  }

  return (
    <div className="flex items-center gap-2 px-6 py-2 border-b border-border/30">
      {/* Triangle = green = start/resume */}
      <button
        onClick={handleStart}
        disabled={state.running && !state.paused}
        className="px-3.5 py-1 text-xs font-medium rounded
          bg-ps-triangle/15 text-ps-triangle border border-ps-triangle/25
          hover:bg-ps-triangle/25 hover:shadow-[0_0_12px_rgba(0,166,81,0.15)]
          disabled:opacity-30 disabled:hover:shadow-none"
      >
        {state.paused ? '▶ Resume' : '▶ Start'}
      </button>
      {/* Square = purple = pause */}
      <button
        onClick={handlePause}
        disabled={!state.running || state.paused}
        className="px-3.5 py-1 text-xs font-medium rounded
          bg-ps-square/15 text-[#c49be0] border border-ps-square/25
          hover:bg-ps-square/25 hover:shadow-[0_0_12px_rgba(155,89,182,0.15)]
          disabled:opacity-30 disabled:hover:shadow-none"
      >
        ⏸ Pause
      </button>
      {/* Circle = red = stop */}
      <button
        onClick={handleStop}
        disabled={!state.running && !state.paused}
        className="px-3.5 py-1 text-xs font-medium rounded
          bg-ps-circle/15 text-[#e06070] border border-ps-circle/25
          hover:bg-ps-circle/25 hover:shadow-[0_0_12px_rgba(193,39,45,0.15)]
          disabled:opacity-30 disabled:hover:shadow-none"
      >
        ⏹ Stop
      </button>
      <div className="w-px h-4 bg-border/40 mx-1" />
      {/* Clear — neutral */}
      <button
        onClick={handleClear}
        className="px-3.5 py-1 text-xs font-medium rounded
          bg-surface-2/50 text-text-3 border border-border/40
          hover:bg-surface-3/60 hover:text-text-2"
      >
        Clear
      </button>
    </div>
  )
}
