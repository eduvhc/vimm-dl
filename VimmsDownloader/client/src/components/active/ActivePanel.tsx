import { useRef, useState } from 'react'
import { useData, useImportQueue, useReorderQueue, exportQueue } from '../../api/queries'
import { useDownload } from '../../hooks/useDownloadState'
import { QueueItem } from './QueueItem'
import { ConvertItem } from './ConvertItem'

export function ActivePanel() {
  const { data } = useData()
  const { state, connection } = useDownload()
  const importMutation = useImportQueue()
  const reorderMutation = useReorderQueue()
  const fileInputRef = useRef<HTMLInputElement>(null)

  const queued = data?.queued ?? []

  const converting = Object.values(state.convStatuses).filter(
    s => ['queued', 'extracting', 'extracted', 'converting'].includes(s.phase)
  )

  // --- Drag and drop state ---
  const [dragId, setDragId] = useState<number | null>(null)
  const [dragOverId, setDragOverId] = useState<number | null>(null)

  function handleDragStart(id: number) {
    setDragId(id)
  }

  function handleDragOver(e: React.DragEvent, id: number) {
    e.preventDefault()
    e.dataTransfer.dropEffect = 'move'
    if (id !== dragId) setDragOverId(id)
  }

  function handleDragLeave() {
    setDragOverId(null)
  }

  function handleDrop(targetId: number) {
    if (dragId == null || dragId === targetId) {
      setDragId(null)
      setDragOverId(null)
      return
    }

    // Build new order: remove dragged item, insert before target
    const ids = queued.map(q => q.id)
    const fromIdx = ids.indexOf(dragId)
    const toIdx = ids.indexOf(targetId)
    if (fromIdx < 0 || toIdx < 0) return

    ids.splice(fromIdx, 1)
    ids.splice(toIdx, 0, dragId)

    reorderMutation.mutate(ids)
    setDragId(null)
    setDragOverId(null)
  }

  function handleDragEnd() {
    setDragId(null)
    setDragOverId(null)
  }

  async function handleExport() {
    const items = await exportQueue()
    const blob = new Blob([JSON.stringify(items, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'queue-export.json'
    a.click()
    URL.revokeObjectURL(url)
  }

  async function handleImport(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    try {
      const text = await file.text()
      const items = JSON.parse(text)
      const result = await importMutation.mutateAsync(items)
      if (result.added > 0 && !state.running && connection) {
        const settingsRes = await fetch('/api/settings')
        const settings = await settingsRes.json()
        connection.invoke('StartDownload', settings.activePath)
      }
    } catch {
      // handled by mutation
    }
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center justify-between px-3 sm:px-6 py-2 bg-surface/30 border-b border-border/20">
        <span className="text-[10px] text-text-4 tracking-wide uppercase">
          {queued.length} queued{converting.length > 0 ? ` · ${converting.length} converting` : ''}
        </span>
        <div className="flex items-center gap-3">
          <button onClick={handleExport}
            className="text-[10px] text-accent/50 hover:text-accent tracking-wide uppercase transition-colors">
            Export
          </button>
          <button onClick={() => fileInputRef.current?.click()}
            className="text-[10px] text-accent/50 hover:text-accent tracking-wide uppercase transition-colors">
            Import
          </button>
          <input ref={fileInputRef} type="file" accept=".json" onChange={handleImport} className="hidden" />
        </div>
      </div>

      <div className="flex-1 overflow-y-auto">
        {converting.map(s => (
          <ConvertItem key={s.itemName} status={s} />
        ))}
        {queued.map(item => (
          <QueueItem
            key={item.id}
            item={item}
            isDragging={dragId === item.id}
            isDragOver={dragOverId === item.id}
            onDragStart={() => handleDragStart(item.id)}
            onDragOver={(e) => handleDragOver(e, item.id)}
            onDragLeave={handleDragLeave}
            onDrop={() => handleDrop(item.id)}
            onDragEnd={handleDragEnd}
          />
        ))}
        {queued.length === 0 && converting.length === 0 && (
          <div className="flex items-center justify-center h-40 text-text-4 text-sm tracking-wide">
            Queue is empty
          </div>
        )}
      </div>
    </div>
  )
}
