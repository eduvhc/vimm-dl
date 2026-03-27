import { useDownload } from '../../hooks/useDownloadState'
import { useDeleteFromQueue, usePatchQueueItem, useSettings } from '../../api/queries'
import { Badge } from '../shared/Badge'
import { ProgressBar } from '../shared/ProgressBar'
import { PlatformIcon } from '../shared/PlatformIcon'
import { slugFromUrl } from '../../lib/format'
import type { QueuedItem, FormatOption } from '../../types/api'

interface QueueItemProps {
  item: QueuedItem
  isDragging: boolean
  isDragOver: boolean
  onDragStart: () => void
  onDragOver: (e: React.DragEvent) => void
  onDragLeave: () => void
  onDrop: () => void
  onDragEnd: () => void
}

export function QueueItem({
  item, isDragging, isDragOver,
  onDragStart, onDragOver, onDragLeave, onDrop, onDragEnd,
}: QueueItemProps) {
  const { state, connection } = useDownload()
  const { data: config } = useSettings()
  const deleteMutation = useDeleteFromQueue()
  const patchMutation = usePatchQueueItem()

  const title = item.title || slugFromUrl(item.url)
  const isActive = state.activeUrl === item.url
  const isDownloading = isActive && state.running && !state.paused
  const isPaused = isActive && state.paused

  const formats: FormatOption[] = item.formats ? JSON.parse(item.formats) : []

  function handlePlay() {
    if (!connection) return
    connection.invoke('StartSpecific', config?.activePath ?? null, item.id)
  }

  function handleDelete() {
    deleteMutation.mutate(item.id)
  }

  function handleFormatChange(e: React.ChangeEvent<HTMLSelectElement>) {
    patchMutation.mutate({ id: item.id, format: parseInt(e.target.value) })
  }

  let badgeVariant: 'downloading' | 'paused' | 'queued' | 'starting' = 'queued'
  let badgeText = 'Queued'
  if (isDownloading && state.activeDlInfo) {
    badgeVariant = 'downloading'
    badgeText = state.activeDlInfo.pct
  } else if (isDownloading) {
    badgeVariant = 'starting'
    badgeText = 'Starting'
  } else if (isPaused) {
    badgeVariant = 'paused'
    badgeText = 'Paused'
  }

  return (
    <div
      draggable={!isActive}
      onDragStart={onDragStart}
      onDragOver={onDragOver}
      onDragLeave={onDragLeave}
      onDrop={onDrop}
      onDragEnd={onDragEnd}
      className={`group flex items-center gap-3 px-5 py-2.5
        border-b transition-all select-none
        ${isActive ? 'xmb-selected xmb-glow border-border/20' : 'border-border/20 hover:bg-card-hover/40'}
        ${isDragging ? 'opacity-30' : ''}
        ${isDragOver ? 'border-t-2 border-t-accent/50' : ''}
        ${!isActive ? 'cursor-grab active:cursor-grabbing' : ''}
      `}
    >
      {/* Drag handle — subtle dots */}
      <div className={`flex flex-col gap-[3px] py-1 opacity-0 group-hover:opacity-40 transition-opacity
        ${isActive ? '!opacity-0' : ''}`}>
        <div className="flex gap-[3px]">
          <div className="w-[3px] h-[3px] rounded-full bg-text-4" />
          <div className="w-[3px] h-[3px] rounded-full bg-text-4" />
        </div>
        <div className="flex gap-[3px]">
          <div className="w-[3px] h-[3px] rounded-full bg-text-4" />
          <div className="w-[3px] h-[3px] rounded-full bg-text-4" />
        </div>
        <div className="flex gap-[3px]">
          <div className="w-[3px] h-[3px] rounded-full bg-text-4" />
          <div className="w-[3px] h-[3px] rounded-full bg-text-4" />
        </div>
      </div>

      <PlatformIcon platform={item.platform} />

      <div className="flex-1 min-w-0">
        <div className="text-sm text-text truncate">{title}</div>
        {item.platform && (
          <div className="text-[10px] text-text-4 truncate">{item.platform}</div>
        )}
      </div>

      {formats.length > 1 && (
        <select
          value={item.format}
          onChange={handleFormatChange}
          className="bg-surface-2/60 border border-border/40 text-text-3 text-[10px] rounded px-1.5 py-0.5
            focus:outline-none focus:border-accent/30"
        >
          {formats.map(f => (
            <option key={f.value} value={f.value}>{f.label}</option>
          ))}
        </select>
      )}

      <span className="text-[11px] font-mono text-text-3 w-16 text-right tabular-nums">
        {item.size || '--'}
      </span>

      {/* Speed */}
      <span className="text-[10px] font-mono text-text-3 w-20 text-right tabular-nums">
        {isDownloading && state.activeDlInfo?.speed ? state.activeDlInfo.speed : ''}
      </span>

      <div className="w-28">
        {(isDownloading && state.activeDlInfo) ? (
          <ProgressBar width={state.activeDlInfo.width} variant="download" />
        ) : isPaused && state.activeDlInfo ? (
          <ProgressBar width={state.activeDlInfo.width} variant="paused" />
        ) : null}
      </div>

      <Badge variant={badgeVariant}>{badgeText}</Badge>

      {/* Actions */}
      <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
        <button
          onClick={handlePlay}
          className="w-6 h-6 flex items-center justify-center rounded
            text-ps-triangle/60 hover:text-ps-triangle hover:bg-ps-triangle/10
            hover:shadow-[0_0_8px_rgba(0,166,81,0.15)] text-xs"
          title="Start"
        >&#9654;</button>
        <button
          onClick={handleDelete}
          className="w-6 h-6 flex items-center justify-center rounded
            text-ps-circle/40 hover:text-ps-circle hover:bg-ps-circle/10
            hover:shadow-[0_0_8px_rgba(193,39,45,0.15)] text-xs"
          title="Remove"
        >&times;</button>
      </div>
    </div>
  )
}
