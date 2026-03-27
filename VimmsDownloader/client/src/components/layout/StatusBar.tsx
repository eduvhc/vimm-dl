import { useDownload } from '../../hooks/useDownloadState'
import { useData } from '../../api/queries'

export function StatusBar() {
  const { state } = useDownload()
  const { data } = useData()

  const queued = data?.queued.length ?? 0
  const completed = data?.history.length ?? 0
  const downloading = state.running && !state.paused ? 1 : 0

  return (
    <footer className="flex items-center justify-between px-6 py-1.5 bg-surface/60 border-t border-border/30
      text-[10px] text-text-4 tracking-wide">
      <div className="flex items-center gap-3">
        <span>{queued} queued</span>
        <span className="text-border-light/40">&middot;</span>
        <span>{downloading} downloading</span>
        <span className="text-border-light/40">&middot;</span>
        <span>{completed} completed</span>
      </div>
      <div className="flex items-center gap-2 uppercase">
        <span>{state.connectionState}</span>
      </div>
    </footer>
  )
}
