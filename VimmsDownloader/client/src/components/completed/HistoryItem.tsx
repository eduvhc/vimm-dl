import { useState } from 'react'
import { useDeleteCompleted, useConvertPs3, usePs3Action } from '../../api/queries'
import { Badge } from '../shared/Badge'
import { PlatformIcon } from '../shared/PlatformIcon'
import { fmtBytes, fmtDuration } from '../../lib/format'
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

const pipelineLabels: Record<string, string> = {
  jb_folder: 'JB Folder',
  dec_iso: 'Dec ISO',
  dec_iso_archive: 'Dec ISO (Archive)',
  none: 'Not converted',
}

function formatLabel(pipelineType: string): string {
  if (pipelineType === 'jb_folder') return '0 — JB Folder (.7z)'
  if (pipelineType === 'dec_iso') return '1 — .dec.iso'
  if (pipelineType === 'dec_iso_archive') return '1 — .dec.iso (from archive)'
  return '—'
}

function StepIndicator({ step }: { step: TraceStep }) {
  const variant = statusBadge[step.status] ?? 'queued'
  const icon = step.status === 'done' ? '\u2713 ' :
    step.status === 'error' ? '\u2717 ' :
    step.status === 'active' ? '\u25B6 ' : ''

  return (
    <div className="flex items-center gap-1.5">
      <Badge variant={variant}>{icon}{step.name}</Badge>
      {step.durationMs != null && step.status === 'done' && (
        <span className="text-[10px] font-mono text-text-4/60">{fmtDuration(step.durationMs)}</span>
      )}
      {step.durationMs != null && step.status === 'active' && (
        <span className="text-[10px] font-mono text-accent/50">{fmtDuration(step.durationMs)}</span>
      )}
      {step.status === 'active' && step.message && (
        <span className="text-[10px] text-text-4 truncate max-w-48">{step.message}</span>
      )}
    </div>
  )
}

function MetaRow({ label, value }: { label: string; value?: string | null }) {
  if (!value) return null
  return (
    <div className="flex items-start gap-2 py-0.5">
      <span className="text-[10px] text-text-4 w-20 shrink-0">{label}</span>
      <span className="text-[11px] text-text-3 font-mono break-all">{value}</span>
    </div>
  )
}

interface HistoryItemProps {
  item: HistoryItemType
  showEventsLink?: boolean
  onViewEvents?: (itemName: string) => void
}

export function HistoryItem({ item, showEventsLink, onViewEvents }: HistoryItemProps) {
  const deleteMutation = useDeleteCompleted()
  const convertMutation = useConvertPs3()
  const actionMutation = usePs3Action()
  const [expanded, setExpanded] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)

  const title = item.title || item.filename
  const trace = item.trace

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

  const badge = trace
    ? <Badge variant={overallBadge}>{overallText}</Badge>
    : <Badge variant={item.fileExists ? 'queued' : 'error'}>{item.fileExists ? 'Downloaded' : 'Missing'}</Badge>

  function handleRowClick(e: React.MouseEvent) {
    // Don't toggle expand when clicking buttons
    if ((e.target as HTMLElement).closest('button')) return
    setExpanded(v => !v)
  }

  return (
    <div className="group border-b border-border/20 hover:bg-card-hover/40 transition-all">
      {/* Desktop row */}
      <div className="hidden sm:flex items-center gap-3 px-5 py-2.5 cursor-pointer" onClick={handleRowClick}>
        <PlatformIcon platform={item.platform} />
        <div className="flex-1 min-w-0">
          <div className="text-sm text-text truncate">{title}</div>
          <div className="flex items-center gap-2 text-[10px] text-text-4">
            <span className="truncate font-mono">{item.filename}</span>
            {item.completedAt && <span>&middot; {item.completedAt}</span>}
          </div>
          {trace && trace.steps.length > 0 && (
            <div className="flex items-center gap-2 mt-1 flex-wrap">
              {trace.steps.map((step, i) => (
                <div key={step.name} className="flex items-center gap-2">
                  {i > 0 && <span className="text-text-4/40">&rarr;</span>}
                  <StepIndicator step={step} />
                </div>
              ))}
            </div>
          )}
          {trace?.isoFilename && overallStatus === 'done' && (
            <div className="flex items-center gap-1 text-[10px] text-ps-triangle/70 mt-0.5">
              <span>&#10003;</span>
              <span className="truncate font-mono">{trace.isoFilename}</span>
              {trace.isoSize != null && <span>({fmtBytes(trace.isoSize)})</span>}
            </div>
          )}
          {overallStatus === 'error' && lastStep?.message && (
            <div className="text-[10px] text-ps-circle/70 mt-0.5 truncate">{lastStep.message}</div>
          )}
        </div>
        <span className="text-[11px] font-mono text-text-3 w-16 text-right tabular-nums">
          {item.fileSize ? fmtBytes(item.fileSize) : item.size || '--'}
        </span>
        {badge}
        <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
          <Actions trace={trace} item={item} convertMutation={convertMutation}
            actionMutation={actionMutation} onDelete={() => setConfirmDelete(v => !v)} />
        </div>
        <span className={`text-[10px] text-text-4/40 transition-transform ${expanded ? 'rotate-90' : ''}`}>&#9654;</span>
      </div>

      {/* Mobile stacked */}
      <div className="sm:hidden px-3 py-2 cursor-pointer space-y-1.5" onClick={handleRowClick}>
        <div className="flex items-start gap-2">
          <PlatformIcon platform={item.platform} />
          <div className="flex-1 min-w-0">
            <div className="text-sm text-text truncate">{title}</div>
            <div className="text-[10px] text-text-4 truncate font-mono">{item.filename}</div>
          </div>
          <span className={`text-[10px] text-text-4/40 transition-transform mt-1 ${expanded ? 'rotate-90' : ''}`}>&#9654;</span>
        </div>
        {trace && trace.steps.length > 0 && (
          <div className="flex items-center gap-1.5 flex-wrap">
            {trace.steps.map((step, i) => (
              <div key={step.name} className="flex items-center gap-1.5">
                {i > 0 && <span className="text-text-4/40">&rarr;</span>}
                <StepIndicator step={step} />
              </div>
            ))}
          </div>
        )}
        {trace?.isoFilename && overallStatus === 'done' && (
          <div className="flex items-center gap-1 text-[10px] text-ps-triangle/70">
            <span>&#10003;</span>
            <span className="truncate font-mono">{trace.isoFilename}</span>
          </div>
        )}
        {overallStatus === 'error' && lastStep?.message && (
          <div className="text-[10px] text-ps-circle/70 truncate">{lastStep.message}</div>
        )}
        <div className="flex items-center gap-2">
          {badge}
          {item.completedAt && <span className="text-[10px] text-text-4">{item.completedAt}</span>}
          <div className="flex items-center gap-1 ml-auto">
            <Actions trace={trace} item={item} convertMutation={convertMutation}
              actionMutation={actionMutation} onDelete={() => setConfirmDelete(v => !v)} />
          </div>
        </div>
      </div>

      {/* Expandable detail */}
      {expanded && (
        <div className="px-3 sm:px-5 pb-3 pt-1 border-t border-border/10 bg-surface/20 space-y-2">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-6">
            {item.platform && <MetaRow label="Platform" value={item.platform} />}
            {trace && trace.pipelineType !== 'none' && (
              <>
                <MetaRow label="Pipeline" value={pipelineLabels[trace.pipelineType] ?? trace.pipelineType} />
                <MetaRow label="Format" value={formatLabel(trace.pipelineType)} />
              </>
            )}
            <MetaRow label="Archive" value={item.filename} />
            {trace?.isoFilename && <MetaRow label="ISO" value={trace.isoFilename} />}
            {item.fileSize != null && <MetaRow label="Archive size" value={fmtBytes(item.fileSize)} />}
            {trace?.isoSize != null && <MetaRow label="ISO size" value={fmtBytes(trace.isoSize)} />}
            {item.completedAt && <MetaRow label="Completed" value={item.completedAt} />}
            {item.filepath && <MetaRow label="Path" value={item.filepath} />}
          </div>

          {showEventsLink && onViewEvents && (
            <button
              onClick={(e) => { e.stopPropagation(); onViewEvents(item.filename) }}
              className="w-full sm:w-auto text-[10px] px-3 py-1.5 rounded bg-accent/10 text-accent/80
                border border-accent/20 hover:bg-accent/15 hover:text-accent transition-colors
                tracking-wide uppercase"
            >
              View Events
            </button>
          )}
        </div>
      )}

      {/* Delete confirmation */}
      {confirmDelete && (
        <div className="flex items-center gap-2 px-3 sm:px-5 py-2 bg-ps-circle/5 border-t border-ps-circle/10 flex-wrap">
          <span className="text-[10px] text-text-3">Remove?</span>
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

function Actions({ trace, item, convertMutation, actionMutation, onDelete }: {
  trace: HistoryItemType['trace']
  item: HistoryItemType
  convertMutation: ReturnType<typeof useConvertPs3>
  actionMutation: ReturnType<typeof usePs3Action>
  onDelete: () => void
}) {
  return (
    <>
      {trace?.actions.includes('convert') && (
        <button onClick={() => convertMutation.mutate(item.filename)}
          className="text-[10px] px-1.5 py-0.5 rounded text-ps-cross/60 hover:text-[#7eb3e0]
            hover:bg-ps-cross/10 transition-colors">Convert</button>
      )}
      {trace?.actions.includes('mark-done') && (
        <button onClick={() => actionMutation.mutate({ filename: item.filename, action: 'mark-done' })}
          className="text-[10px] px-1.5 py-0.5 rounded text-text-4 hover:text-text-2
            hover:bg-surface-3/40 transition-colors">Done</button>
      )}
      {trace?.actions.includes('retry') && (
        <button onClick={() => convertMutation.mutate(item.filename)}
          className="text-[10px] px-1.5 py-0.5 rounded text-amber/60 hover:text-amber
            hover:bg-amber/10 transition-colors">Retry</button>
      )}
      {trace?.actions.includes('abort') && (
        <button onClick={() => actionMutation.mutate({ filename: item.filename, action: 'abort' })}
          className="text-[10px] px-1.5 py-0.5 rounded text-ps-circle/60 hover:text-ps-circle
            hover:bg-ps-circle/10 transition-colors">Abort</button>
      )}
      <button onClick={onDelete}
        className="w-6 h-6 flex items-center justify-center rounded
          text-ps-circle/30 hover:text-ps-circle hover:bg-ps-circle/10 text-xs"
        title="Remove">&times;</button>
    </>
  )
}
