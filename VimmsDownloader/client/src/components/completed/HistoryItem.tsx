import { useState } from 'react'
import { useDeleteCompleted, useConvertPs3, usePs3Action } from '../../api/queries'
import { Badge } from '../shared/Badge'
import { PlatformIcon } from '../shared/PlatformIcon'
import { fmtBytes } from '../../lib/format'
import type { HistoryItem as HistoryItemType, TraceStep } from '../../types/api'

type BadgeVariant = 'downloading' | 'paused' | 'queued' | 'starting' |
  'extracting' | 'converting' | 'done' | 'error' | 'skipped' | 'waiting'

const statusBadge: Record<string, BadgeVariant> = {
  pending: 'queued',
  active: 'converting',
  done: 'done',
  error: 'error',
  skipped: 'skipped',
}

function StepIndicator({ step }: { step: TraceStep }) {
  const variant = statusBadge[step.status] ?? 'queued'
  const icon = step.status === 'done' ? '\u2713 ' :
    step.status === 'error' ? '\u2717 ' :
    step.status === 'active' ? '\u25B6 ' : ''

  return (
    <div className="flex items-center gap-1.5">
      <Badge variant={variant}>{icon}{step.name}</Badge>
      {step.status === 'active' && step.message && (
        <span className="text-[10px] text-text-4 truncate max-w-48">{step.message}</span>
      )}
    </div>
  )
}

export function HistoryItem({ item }: { item: HistoryItemType }) {
  const deleteMutation = useDeleteCompleted()
  const convertMutation = useConvertPs3()
  const actionMutation = usePs3Action()
  const [confirmDelete, setConfirmDelete] = useState(false)

  const title = item.title || item.filename
  const trace = item.trace

  // Overall status from last step
  const lastStep = trace?.steps.at(-1)
  const overallStatus = lastStep?.status ?? (item.fileExists ? 'none' : 'missing')

  const overallBadge: BadgeVariant =
    overallStatus === 'done' ? 'done' :
    overallStatus === 'error' ? 'error' :
    overallStatus === 'active' ? 'converting' :
    overallStatus === 'skipped' ? 'skipped' :
    'queued'

  const overallText =
    overallStatus === 'done' ? 'ISO Ready' :
    overallStatus === 'error' ? 'Failed' :
    overallStatus === 'active' ? 'Converting' :
    overallStatus === 'skipped' ? 'Skipped' :
    overallStatus === 'pending' ? 'Queued' :
    item.fileExists ? 'Downloaded' : 'Missing'

  return (
    <div className="group border-b border-border/20 hover:bg-card-hover/40 transition-all">
      <div className="flex items-center gap-3 px-5 py-2.5">
        <PlatformIcon platform={item.platform} />

        <div className="flex-1 min-w-0">
          <div className="text-sm text-text truncate">{title}</div>
          <div className="flex items-center gap-2 text-[10px] text-text-4">
            <span className="truncate font-mono">{item.filename}</span>
            {item.completedAt && <span>&middot; {item.completedAt}</span>}
          </div>

          {/* Trace steps */}
          {trace && trace.steps.length > 0 && (
            <div className="flex items-center gap-2 mt-1">
              {trace.steps.map((step, i) => (
                <div key={step.name} className="flex items-center gap-2">
                  {i > 0 && <span className="text-text-4/40">&rarr;</span>}
                  <StepIndicator step={step} />
                </div>
              ))}
            </div>
          )}

          {/* ISO output */}
          {trace?.isoFilename && overallStatus === 'done' && (
            <div className="flex items-center gap-1 text-[10px] text-ps-triangle/70 mt-0.5">
              <span>&#10003;</span>
              <span className="truncate font-mono">{trace.isoFilename}</span>
              {trace.isoSize != null && <span>({fmtBytes(trace.isoSize)})</span>}
            </div>
          )}

          {/* Error message */}
          {overallStatus === 'error' && lastStep?.message && (
            <div className="text-[10px] text-ps-circle/70 mt-0.5 truncate">
              {lastStep.message}
            </div>
          )}
        </div>

        <span className="text-[11px] font-mono text-text-3 w-16 text-right tabular-nums">
          {item.fileSize ? fmtBytes(item.fileSize) : item.size || '--'}
        </span>

        {trace ? (
          <Badge variant={overallBadge}>{overallText}</Badge>
        ) : (
          <Badge variant={item.fileExists ? 'queued' : 'error'}>
            {item.fileExists ? 'Downloaded' : 'Missing'}
          </Badge>
        )}

        {/* Actions */}
        <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
          {trace?.actions.includes('convert') && (
            <button onClick={() => convertMutation.mutate(item.filename)}
              className="text-[10px] px-1.5 py-0.5 rounded text-ps-cross/60 hover:text-[#7eb3e0]
                hover:bg-ps-cross/10 transition-colors">
              Convert
            </button>
          )}
          {trace?.actions.includes('mark-done') && (
            <button onClick={() => actionMutation.mutate({ filename: item.filename, action: 'mark-done' })}
              className="text-[10px] px-1.5 py-0.5 rounded text-text-4 hover:text-text-2
                hover:bg-surface-3/40 transition-colors">
              Mark Done
            </button>
          )}
          {trace?.actions.includes('retry') && (
            <button onClick={() => convertMutation.mutate(item.filename)}
              className="text-[10px] px-1.5 py-0.5 rounded text-amber/60 hover:text-amber
                hover:bg-amber/10 transition-colors">
              Retry
            </button>
          )}
          {trace?.actions.includes('abort') && (
            <button onClick={() => actionMutation.mutate({ filename: item.filename, action: 'abort' })}
              className="text-[10px] px-1.5 py-0.5 rounded text-ps-circle/60 hover:text-ps-circle
                hover:bg-ps-circle/10 transition-colors">
              Abort
            </button>
          )}
          <button onClick={() => setConfirmDelete(v => !v)}
            className="w-6 h-6 flex items-center justify-center rounded
              text-ps-circle/30 hover:text-ps-circle hover:bg-ps-circle/10 text-xs"
            title="Remove">&times;</button>
        </div>
      </div>

      {/* Inline delete confirmation */}
      {confirmDelete && (
        <div className="flex items-center gap-3 px-5 py-2 bg-ps-circle/5 border-t border-ps-circle/10">
          <span className="text-[10px] text-text-3">Remove this entry?</span>
          <button
            onClick={() => { deleteMutation.mutate({ id: item.id }); setConfirmDelete(false) }}
            className="text-[10px] px-2 py-0.5 rounded bg-surface-3/50 text-text-3
              hover:bg-surface-3 hover:text-text transition-colors">
            Record only
          </button>
          {item.fileExists && (
            <button
              onClick={() => { deleteMutation.mutate({ id: item.id, deleteFiles: true }); setConfirmDelete(false) }}
              className="text-[10px] px-2 py-0.5 rounded bg-ps-circle/15 text-[#e06070]
                hover:bg-ps-circle/25 transition-colors">
              Delete files too
            </button>
          )}
          <button
            onClick={() => setConfirmDelete(false)}
            className="text-[10px] text-text-4 hover:text-text-3 transition-colors ml-auto">
            Cancel
          </button>
        </div>
      )}
    </div>
  )
}
