import { usePs3Action } from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { Badge } from '../shared/Badge'
import { ProgressBar } from '../shared/ProgressBar'
import { fmtDuration } from '../../lib/format'
import type { Ps3IsoStatusEvent } from '../../types/signalr'

interface ConvertItemProps {
  status: Ps3IsoStatusEvent
}

export function ConvertItem({ status }: ConvertItemProps) {
  const actionMutation = usePs3Action()
  const { state } = useDownload()
  const startTime = state.convStartTimes[status.itemName]
  const elapsed = startTime ? Date.now() - startTime : null

  const pctMatch = status.message.match(/(\d+)%/)
  const pct = pctMatch ? parseInt(pctMatch[1]) : null

  let badgeVariant: 'extracting' | 'converting' | 'waiting' = 'waiting'
  let badgeText = status.phase
  if (status.phase === 'extracting') {
    badgeVariant = 'extracting'
    badgeText = pct !== null ? `Extract ${pct}%` : 'Extracting'
  } else if (status.phase === 'converting') {
    badgeVariant = 'converting'
    badgeText = 'Converting'
  } else if (status.phase === 'extracted') {
    badgeVariant = 'waiting'
    badgeText = 'Waiting'
  } else if (status.phase === 'queued') {
    badgeVariant = 'waiting'
    badgeText = 'Queued'
  }

  return (
    <div className="flex items-center gap-2 sm:gap-3 px-3 sm:px-5 py-2 sm:py-2.5 border-b border-border/20
      bg-surface-2/20 border-l-2 border-l-amber/30">
      <div className="hidden sm:block w-5" />

      <div className="flex-1 min-w-0">
        <div className="text-sm text-text truncate">{status.itemName}</div>
        <div className="text-[10px] text-text-4 truncate">{status.message}</div>
      </div>

      <div className="hidden sm:block w-28">
        {pct !== null && (
          <ProgressBar width={`${pct}%`} variant={status.phase === 'extracting' ? 'extract' : 'convert'} />
        )}
      </div>

      <Badge variant={badgeVariant}>{badgeText}</Badge>

      {elapsed != null && elapsed > 1000 && (
        <span className="hidden sm:inline text-[10px] font-mono text-text-4/50 tabular-nums w-14 text-right">
          {fmtDuration(elapsed)}
        </span>
      )}

      {/* Circle = red = abort */}
      <button
        onClick={() => actionMutation.mutate({ filename: status.itemName, action: 'abort' })}
        className="w-6 h-6 flex items-center justify-center rounded
          text-ps-circle/40 hover:text-ps-circle hover:bg-ps-circle/10 text-xs"
        title="Abort"
      >&#9632;</button>
    </div>
  )
}
