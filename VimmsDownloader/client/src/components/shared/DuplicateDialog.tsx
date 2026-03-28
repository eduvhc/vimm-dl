import type { DuplicateInfo } from '../../types/api'
import { slugFromUrl } from '../../lib/format'

interface DuplicateDialogProps {
  duplicates: DuplicateInfo[]
  onConfirm: () => void
  onCancel: () => void
  isPending: boolean
}

function FileStatus({ d }: { d: DuplicateInfo }) {
  if (d.source === 'queued') return null

  const parts: { label: string; exists: boolean }[] = []
  if (d.filename) parts.push({ label: d.filename, exists: d.archiveExists })
  if (d.isoFilename) parts.push({ label: d.isoFilename, exists: d.isoExists })

  if (parts.length === 0) return null

  return (
    <div className="mt-1 space-y-0.5">
      {parts.map(p => (
        <div key={p.label} className="flex items-center gap-1.5 text-[10px] font-mono truncate">
          <span className={p.exists ? 'text-ps-triangle/70' : 'text-text-4/50 line-through'}>
            {p.exists ? '\u2713' : '\u2717'}
          </span>
          <span className={p.exists ? 'text-text-3' : 'text-text-4/50'}>{p.label}</span>
        </div>
      ))}
    </div>
  )
}

export function DuplicateDialog({ duplicates, onConfirm, onCancel, isPending }: DuplicateDialogProps) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4"
      onClick={onCancel}>
      <div className="bg-surface border border-border rounded-lg shadow-2xl w-full max-w-lg max-h-[80vh] flex flex-col"
        onClick={e => e.stopPropagation()}>

        <div className="px-4 sm:px-5 py-3 border-b border-border/40">
          <h2 className="text-sm font-semibold text-text">Duplicates found</h2>
          <p className="text-[11px] text-text-3 mt-0.5">
            {duplicates.length === 1 ? 'This URL already exists.' : `${duplicates.length} URLs already exist.`}
            {' '}Add anyway?
          </p>
        </div>

        <div className="flex-1 overflow-y-auto px-4 sm:px-5 py-3 space-y-2">
          {duplicates.map(d => (
            <div key={`${d.url}-${d.source}`}
              className="p-2.5 rounded border border-border/30 bg-card/40">
              <div className="text-xs text-text truncate">
                {d.title || slugFromUrl(d.url)}
              </div>
              <div className="flex items-center gap-2 mt-1 flex-wrap">
                <span className={`text-[10px] px-1.5 py-0.5 rounded border font-medium tracking-wide ${
                  d.source === 'queued'
                    ? 'bg-amber/12 text-amber border-amber/20'
                    : 'bg-ps-triangle/12 text-ps-triangle border-ps-triangle/20'
                }`}>
                  {d.source === 'queued' ? 'IN QUEUE' : 'COMPLETED'}
                </span>
                <span className="text-[10px] text-text-3">{d.reason}</span>
              </div>
              <FileStatus d={d} />
            </div>
          ))}
        </div>

        <div className="flex items-center justify-end gap-2 px-4 sm:px-5 py-3 border-t border-border/40">
          <button onClick={onCancel}
            className="px-3.5 py-1.5 text-xs font-medium rounded bg-surface-2/50 text-text-3
              border border-border/40 hover:bg-surface-3/60 hover:text-text-2 transition-all">
            Cancel
          </button>
          <button onClick={onConfirm} disabled={isPending}
            className="px-3.5 py-1.5 text-xs font-medium rounded bg-ps-cross/20 text-[#7eb3e0]
              border border-ps-cross/30 hover:bg-ps-cross/30
              hover:shadow-[0_0_16px_rgba(46,109,180,0.2)] transition-all disabled:opacity-40">
            {isPending ? 'Adding...' : 'Add Anyway'}
          </button>
        </div>
      </div>
    </div>
  )
}
