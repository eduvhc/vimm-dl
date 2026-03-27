import { useDeleteCompleted, useConvertPs3, usePs3Action } from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { Badge } from '../shared/Badge'
import { PlatformIcon } from '../shared/PlatformIcon'
import { fmtBytes } from '../../lib/format'
import type { HistoryItem as HistoryItemType } from '../../types/api'

interface HistoryItemProps {
  item: HistoryItemType
}

export function HistoryItem({ item }: HistoryItemProps) {
  const { state } = useDownload()
  const deleteMutation = useDeleteCompleted()
  const convertMutation = useConvertPs3()
  const actionMutation = usePs3Action()

  const title = item.title || item.filename
  const convStatus = state.convStatuses[item.filename]
  const phase = convStatus?.phase ?? item.convPhase
  const isPs3 = item.platform?.toLowerCase() === 'playstation 3'

  let isoStatus: 'ready' | 'error' | 'converting' | 'skipped' | 'none' = 'none'
  if (item.isoExists || phase === 'done') isoStatus = 'ready'
  else if (phase === 'error') isoStatus = 'error'
  else if (['extracting', 'converting', 'queued', 'extracted'].includes(phase ?? '')) isoStatus = 'converting'
  else if (phase === 'skipped') isoStatus = 'skipped'

  const badgeVariant =
    isoStatus === 'ready' ? 'done' :
    isoStatus === 'error' ? 'error' :
    isoStatus === 'converting' ? 'converting' :
    'queued'

  const badgeText =
    isoStatus === 'ready' ? 'ISO Ready' :
    isoStatus === 'error' ? 'Failed' :
    isoStatus === 'converting' ? (phase ?? 'Processing') :
    isoStatus === 'skipped' ? 'Skipped' :
    item.fileExists ? 'Downloaded' : 'Missing'

  return (
    <div className="group flex items-center gap-3 px-5 py-2.5
      border-b border-border/20 hover:bg-card-hover/40 transition-all">
      <PlatformIcon platform={item.platform} />

      <div className="flex-1 min-w-0">
        <div className="text-sm text-text truncate">{title}</div>
        <div className="flex items-center gap-2 text-[10px] text-text-4">
          <span className="truncate font-mono">{item.filename}</span>
          {item.completedAt && <span>&middot; {item.completedAt}</span>}
        </div>
        {isPs3 && isoStatus === 'ready' && item.isoFilename && (
          <div className="flex items-center gap-1 text-[10px] text-ps-triangle/70 mt-0.5">
            <span>&#10003;</span>
            <span className="truncate font-mono">{item.isoFilename}</span>
            {item.isoSize && <span>({fmtBytes(item.isoSize)})</span>}
          </div>
        )}
        {isPs3 && isoStatus === 'error' && (
          <div className="text-[10px] text-ps-circle/70 mt-0.5">
            {convStatus?.message ?? item.convMessage ?? 'Conversion failed'}
          </div>
        )}
        {isPs3 && isoStatus === 'converting' && (
          <div className="text-[10px] text-amber/70 mt-0.5">
            {convStatus?.message ?? 'Processing...'}
          </div>
        )}
      </div>

      <span className="text-[11px] font-mono text-text-3 w-16 text-right tabular-nums">
        {item.fileSize ? fmtBytes(item.fileSize) : item.size || '--'}
      </span>

      <Badge variant={badgeVariant}>{badgeText}</Badge>

      <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
        {isPs3 && isoStatus === 'none' && item.fileExists && (
          <>
            {/* Cross = blue = convert action */}
            <button onClick={() => convertMutation.mutate(item.filename)}
              className="text-[10px] px-1.5 py-0.5 rounded text-ps-cross/60 hover:text-[#7eb3e0]
                hover:bg-ps-cross/10 transition-colors">
              Convert
            </button>
            <button onClick={() => actionMutation.mutate({ filename: item.filename, action: 'mark-done' })}
              className="text-[10px] px-1.5 py-0.5 rounded text-text-4 hover:text-text-2
                hover:bg-surface-3/40 transition-colors">
              Mark Done
            </button>
          </>
        )}
        {isPs3 && isoStatus === 'error' && (
          <button onClick={() => convertMutation.mutate(item.filename)}
            className="text-[10px] px-1.5 py-0.5 rounded text-amber/60 hover:text-amber
              hover:bg-amber/10 transition-colors">
            Retry
          </button>
        )}
        {isPs3 && isoStatus === 'converting' && (
          <button onClick={() => actionMutation.mutate({ filename: item.filename, action: 'abort' })}
            className="text-[10px] px-1.5 py-0.5 rounded text-ps-circle/60 hover:text-ps-circle
              hover:bg-ps-circle/10 transition-colors">
            Abort
          </button>
        )}
        {!item.fileExists && (
          <button onClick={() => deleteMutation.mutate(item.id)}
            className="w-6 h-6 flex items-center justify-center rounded
              text-ps-circle/30 hover:text-ps-circle hover:bg-ps-circle/10 text-xs"
            title="Remove">&times;</button>
        )}
      </div>
    </div>
  )
}
