import { useSyncCopy } from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { fmtBytes } from '../../lib/format'
import { ProgressBar } from '../shared/ProgressBar'
import type { SyncFileInfo } from '../../types/api'

interface SyncFileRowProps {
  file: SyncFileInfo
  status: 'new' | 'synced' | 'target-only'
}

export function SyncFileRow({ file, status }: SyncFileRowProps) {
  const { state } = useDownload()
  const copyMutation = useSyncCopy()

  const copyProgress = state.syncCopying[file.name]
  const isCopying = !!copyProgress

  const statusColor =
    status === 'new' ? 'text-ps-triangle' :
    status === 'synced' ? 'text-accent/60' : 'text-text-4'

  const statusLabel =
    status === 'new' ? 'NEW' :
    status === 'synced' ? 'SYNCED' : 'TARGET'

  return (
    <div className="flex items-center gap-3 px-5 py-1.5 border-b border-border/15
      hover:bg-card-hover/30 transition-all">
      <span className={`text-[10px] font-medium w-14 tracking-wide ${statusColor}`}>
        {statusLabel}
      </span>
      <span className="flex-1 text-xs text-text truncate font-mono">{file.name}</span>
      <span className="text-[10px] font-mono text-text-3 w-16 text-right tabular-nums">
        {fmtBytes(file.size)}
      </span>

      {isCopying && (
        <div className="w-24">
          <ProgressBar width={`${copyProgress.percent}%`} variant="sync" />
        </div>
      )}

      {status === 'new' && !isCopying && (
        <button
          onClick={() => copyMutation.mutate(file.name)}
          disabled={copyMutation.isPending}
          className="text-[10px] px-2 py-0.5 rounded text-ps-cross/60 hover:text-[#7eb3e0]
            hover:bg-ps-cross/10 border border-ps-cross/20 transition-colors"
        >
          Copy
        </button>
      )}
    </div>
  )
}
