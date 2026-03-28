import { useState } from 'react'
import { Badge } from '../shared/Badge'
import type { EventRow } from '../../types/api'

type BadgeVariant = 'downloading' | 'paused' | 'queued' | 'starting' |
  'extracting' | 'converting' | 'done' | 'error' | 'skipped' | 'waiting'

function getBadge(e: EventRow): { variant: BadgeVariant; label: string } {
  if (e.eventType === 'download_completed') return { variant: 'done', label: 'Downloaded' }
  if (e.eventType === 'download_error') return { variant: 'error', label: 'Error' }
  if (e.eventType === 'download_progress') return { variant: 'downloading', label: 'Progress' }
  if (e.eventType === 'download_status') return { variant: 'starting', label: 'Status' }
  if (e.eventType === 'download_done') return { variant: 'done', label: 'Queue Done' }
  if (e.eventType === 'sync_progress') return { variant: 'downloading', label: 'Sync' }
  if (e.eventType === 'sync_completed') {
    const failed = e.message?.startsWith('Failed')
    return failed ? { variant: 'error', label: 'Sync Fail' } : { variant: 'done', label: 'Synced' }
  }
  if (e.eventType === 'pipeline_status') {
    if (e.phase === 'done') return { variant: 'done', label: 'Converted' }
    if (e.phase === 'error') return { variant: 'error', label: 'Error' }
    if (e.phase === 'extracting') return { variant: 'extracting', label: 'Extracting' }
    if (e.phase === 'converting') return { variant: 'converting', label: 'Converting' }
    if (e.phase === 'skipped') return { variant: 'skipped', label: 'Skipped' }
    if (e.phase === 'extracted') return { variant: 'waiting', label: 'Extracted' }
    return { variant: 'queued', label: 'Queued' }
  }
  return { variant: 'queued', label: e.eventType }
}

function formatTimestamp(ts: string): string {
  try {
    const d = new Date(ts + 'Z')
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })
      + ' ' + d.toLocaleDateString([], { month: 'short', day: 'numeric' })
  } catch {
    return ts
  }
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false)

  function handleCopy() {
    navigator.clipboard.writeText(text)
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  return (
    <button onClick={handleCopy}
      className="text-[10px] px-1.5 py-0.5 rounded text-text-4 hover:text-text-2
        hover:bg-surface-3/40 transition-colors shrink-0">
      {copied ? 'Copied' : 'Copy'}
    </button>
  )
}

export function EventItem({ event: e }: { event: EventRow }) {
  const [expanded, setExpanded] = useState(false)
  const badge = getBadge(e)

  return (
    <div className="border-b border-border/10 hover:bg-card-hover/30 transition-colors">
      <div
        className="flex items-center gap-2 sm:gap-3 px-3 sm:px-5 py-1.5 cursor-pointer"
        onClick={() => setExpanded(v => !v)}
      >
        <span className="hidden sm:inline text-[10px] font-mono text-text-4 w-28 shrink-0 tabular-nums">
          {formatTimestamp(e.timestamp)}
        </span>

        <div className="w-16 sm:w-20 shrink-0">
          <Badge variant={badge.variant}>{badge.label}</Badge>
        </div>

        <span className="text-[11px] text-text-3 sm:w-44 shrink-0 truncate min-w-0 flex-1 sm:flex-none" title={e.itemName}>
          {e.itemName}
        </span>

        <span className="hidden sm:inline text-[11px] text-text-4 truncate flex-1">
          {e.message}
        </span>

        <span className={`text-[10px] text-text-4/50 transition-transform shrink-0 ${expanded ? 'rotate-90' : ''}`}>
          &#9654;
        </span>
      </div>

      {expanded && (
        <div className="px-3 sm:px-5 pb-2 pt-0.5 sm:ml-28 space-y-1.5">
          <div className="flex items-start gap-2">
            <span className="text-[10px] text-text-4 w-16 shrink-0">Item</span>
            <span className="text-[11px] text-text-3 font-mono break-all flex-1">{e.itemName}</span>
            <CopyButton text={e.itemName} />
          </div>
          <div className="flex items-start gap-2">
            <span className="text-[10px] text-text-4 w-16 shrink-0">Message</span>
            <span className="text-[11px] text-text-3 break-all flex-1">{e.message ?? '-'}</span>
            {e.message && <CopyButton text={e.message} />}
          </div>
          {e.phase && (
            <div className="flex items-start gap-2">
              <span className="text-[10px] text-text-4 w-16 shrink-0">Phase</span>
              <span className="text-[11px] text-text-3 font-mono">{e.phase}</span>
            </div>
          )}
          {e.data && (
            <div className="flex items-start gap-2">
              <span className="text-[10px] text-text-4 w-16 shrink-0">Data</span>
              <pre className="text-[10px] text-text-4 font-mono break-all flex-1 whitespace-pre-wrap">{e.data}</pre>
              <CopyButton text={e.data} />
            </div>
          )}
          <div className="flex items-start gap-2">
            <span className="text-[10px] text-text-4 w-16 shrink-0">Type</span>
            <span className="text-[11px] text-text-4 font-mono">{e.eventType}</span>
          </div>
          {e.correlationId && (
            <div className="flex items-start gap-2">
              <span className="text-[10px] text-text-4 w-16 shrink-0">Run ID</span>
              <span className="text-[11px] text-text-4 font-mono">{e.correlationId}</span>
              <CopyButton text={e.correlationId} />
            </div>
          )}
        </div>
      )}
    </div>
  )
}
