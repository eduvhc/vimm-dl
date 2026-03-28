import { useState } from 'react'
import { useSyncCompare, useSyncCopy, useSyncCancel } from '../../api/queries'
import { SyncDiskCard } from './SyncDiskCard'
import { SyncFileRow } from './SyncFileRow'
import type { SyncCompareResponse } from '../../types/api'

export function SyncPanel() {
  const [path, setPath] = useState('')
  const [syncData, setSyncData] = useState<SyncCompareResponse | null>(null)
  const compareMutation = useSyncCompare()
  const copyAllMutation = useSyncCopy()
  const cancelMutation = useSyncCancel()

  function handleCompare() {
    if (!path.trim()) return
    compareMutation.mutate(path, { onSuccess: data => setSyncData(data) })
  }

  const newFiles = syncData?.new ?? []
  const syncedFiles = syncData?.synced ?? []
  const targetOnlyFiles = syncData?.targetOnly ?? []

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center gap-2 px-3 sm:px-6 py-2.5 bg-surface/30 border-b border-border/20 flex-wrap">
        <input
          type="text"
          value={path}
          onChange={e => setPath(e.target.value)}
          placeholder="Target path (e.g. H:\PS3ISO)"
          className="flex-1 bg-surface/60 border border-border/40 rounded px-3 py-1 text-sm text-text
            placeholder:text-text-4 focus:outline-none focus:border-accent/30
            focus:shadow-[0_0_10px_rgba(91,155,213,0.08)]"
        />
        <button onClick={handleCompare} disabled={compareMutation.isPending}
          className="px-3.5 py-1 text-xs font-medium rounded bg-ps-cross/15 text-[#7eb3e0]
            border border-ps-cross/25 hover:bg-ps-cross/25
            hover:shadow-[0_0_12px_rgba(46,109,180,0.15)] disabled:opacity-40">
          Compare
        </button>
        {newFiles.length > 0 && (
          <button onClick={() => copyAllMutation.mutate(undefined)}
            className="px-3.5 py-1 text-xs font-medium rounded bg-ps-triangle/15 text-ps-triangle
              border border-ps-triangle/25 hover:bg-ps-triangle/25
              hover:shadow-[0_0_12px_rgba(0,166,81,0.15)]">
            Copy All
          </button>
        )}
        <button onClick={() => cancelMutation.mutate()}
          className="px-3.5 py-1 text-xs font-medium rounded bg-surface-2/40 text-text-3
            border border-border/30 hover:bg-ps-circle/10 hover:text-[#e06070] hover:border-ps-circle/20">
          Cancel
        </button>
      </div>

      {syncData?.error && (
        <div className="mx-3 sm:mx-6 mt-2 p-2 bg-ps-circle/8 border border-ps-circle/20 rounded text-xs text-[#e06070]">
          {syncData.error}
        </div>
      )}

      {syncData && (syncData.source || syncData.target) && (
        <div className="flex flex-col sm:flex-row gap-3 px-3 sm:px-6 py-3">
          <SyncDiskCard label="Source" info={syncData.source} />
          <SyncDiskCard label="Target" info={syncData.target} />
        </div>
      )}

      {syncData && (
        <div className="px-3 sm:px-6 pb-2 text-[10px] text-text-3 tracking-wide">
          <span className="text-ps-triangle">{newFiles.length} new</span>
          {' · '}
          <span className="text-accent/60">{syncedFiles.length} synced</span>
          {' · '}
          <span className="text-text-4">{targetOnlyFiles.length} target only</span>
        </div>
      )}

      <div className="flex-1 overflow-y-auto">
        {newFiles.map(f => <SyncFileRow key={f.name} file={f} status="new" />)}
        {syncedFiles.map(f => <SyncFileRow key={f.name} file={f} status="synced" />)}
        {targetOnlyFiles.map(f => <SyncFileRow key={f.name} file={f} status="target-only" />)}
        {!syncData && (
          <div className="flex items-center justify-center h-40 text-text-4 text-sm tracking-wide">
            Enter target path and Compare
          </div>
        )}
      </div>
    </div>
  )
}
